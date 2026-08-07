using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;

/// <summary>
/// SRS FR-7.1 / Sequence Diagram §7 / StateMachine.md §4.3's <c>[*] --ConvertAngebotToProject-->
/// Active</c>. The commercial turning point: BR-2 calls a Project "committed, paid work", so this
/// is the one place a Project may come into existence.
///
/// <para>
/// <b>BR-2's guard lives here, and that is an explicit, approved exception to CLAUDE.md §6's "a
/// handler never checks an aggregate-state field itself".</b> The invariant is about the
/// <i>Angebot's</i> status governing whether a <i>different</i> aggregate may be created, so no
/// single aggregate can own it: <see cref="Project"/> deliberately holds no reference to
/// <c>Angebot</c> (pinned by a reflection test), and putting the check inside
/// <c>Project.Create</c> would require passing the Angebot in and coupling the two roots.
/// `BusinessRules.md` BR-2 names this command as the enforcement point, and StateMachine.md §5
/// repeats it. **Do not move this guard into the Domain and do not edit BR-2 to permit that.**
/// </para>
/// <para>
/// <b>Every rejection is evaluated before anything is created.</b> Validation, then the Angebot
/// must exist, then BR-2, then the already-converted check, then the Lead must exist — only after
/// all five does a single <see cref="Customer"/> or <see cref="Project"/> get constructed. This is
/// the §12 ordering principle applied to entity creation rather than file I/O: a guard that can
/// fire before a mutation should.
/// </para>
/// <para>
/// <b>Customer and Project commit together, and the two paths differ structurally.</b> Reusing an
/// existing Customer needs one <c>SaveChangesAsync</c> for the Project alone, which EF Core's own
/// implicit transaction already makes atomic — no explicit transaction is opened, because symmetry
/// is not a reason to take a lock. Creating a Customer does need one: <c>Project.CustomerId</c>
/// requires a database-generated identity that only a <c>SaveChangesAsync</c> produces, and since
/// <c>Project</c> deliberately has no <c>Customer</c> navigation property, EF cannot defer that
/// foreign key through relationship fix-up. So the Customer is saved first, inside an explicit
/// transaction, and the Project follows in a second save under the same transaction. **The
/// transaction, not the number of saves, is the atomic boundary.** Without it a failure between
/// the two would leave a Customer with no Project — a row asserting the Lead became a customer
/// with nothing to show for it, which the unique index on <c>LeadId</c> would then make
/// un-retryable without manual cleanup. See D48's amendment for the rejected alternatives.
/// </para>
/// <para>
/// <b>No ownership check.</b> `PermissionMatrix.md` §5 marks "Convert Angebot to Project" as
/// Admin **F** — blanket role authority, not a scoped relationship — so calling
/// <c>IOwnershipValidator</c> here would be a semantic error, not merely redundant (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>The Lead is read, never written.</b> Sequence Diagram §7's Phase 6 correction is explicit:
/// <c>Lead.Status</c> is not touched here, because the Lead already reached <c>Won</c> in the
/// customer's decision handler (StateMachine.md §5). There is no second path to <c>Won</c>.
/// </para>
/// </summary>
public sealed class ConvertAngebotToProjectCommandHandler(
    IValidator<ConvertAngebotToProjectCommand> validator,
    IAngebotRepository angebotRepository,
    ILeadRepository leadRepository,
    ICustomerRepository customerRepository,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ConvertAngebotToProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> HandleAsync(ConvertAngebotToProjectCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        // BR-2. A cross-aggregate prerequisite, which is why it is here rather than in the Domain
        // — see the class remarks. ConflictException because the Angebot exists and the caller may
        // act on it, just not yet: CLAUDE.md §17's definition of a state conflict, mapped to 409.
        if (angebot.Status != AngebotStatus.CustomerApproved)
        {
            throw new ConflictException(
                $"Angebot {angebot.Id} is in status '{angebot.Status}' and cannot become a Project; " +
                $"only '{AngebotStatus.CustomerApproved}' may be converted (BR-2).");
        }

        // ERD.md: one Angebot converts to exactly one Project. Checked here so an ordinary second
        // click is a 409 rather than an unmapped DbUpdateException (D62); the unique index on
        // Projects.AngebotId remains the backstop for two conversions racing past this point.
        if (await projectRepository.ExistsForAngebotAsync(angebot.Id, cancellationToken))
        {
            throw new ConflictException($"Angebot {angebot.Id} has already been converted into a Project.");
        }

        var lead = await leadRepository.GetByIdAsync(angebot.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), angebot.LeadId);

        // Find-or-create by LeadId only. No matching by email, phone, name or address: deciding
        // that two Leads are the same person is a customer-identity policy no document states, and
        // getting it wrong merges strangers or splits a real customer. ERD.md records the accepted
        // consequence — a repeat customer arriving as a new Lead gets a second Customer row.
        var existingCustomer = await customerRepository.FindByLeadIdAsync(lead.Id, cancellationToken);

        var project = existingCustomer is null
            ? await CreateCustomerAndProjectAsync(lead, angebot, cancellationToken)
            : await CreateProjectForExistingCustomerAsync(existingCustomer, angebot, cancellationToken);

        // Logged against Project, per Sequence Diagram §7 and Architecture.md §11's audit-target
        // principle: the milestone is the Project coming into existence, and no Lead-level status
        // change happens here at all.
        await auditService.LogAsync(
            entityType: nameof(Project),
            entityId: project.Id,
            action: AuditAction.ProjectCreated,
            performedByUserId: command.PerformedByAdminId,
            details: null,
            cancellationToken);

        return project.ToDto();
    }

    /// <summary>
    /// The path that needs the explicit transaction. The first save is what makes
    /// <c>customer.Id</c> a real positive value; <c>Project.Create</c>'s <c>customerId &gt; 0</c>
    /// guard therefore stays intact rather than being weakened to accommodate persistence.
    ///
    /// <para>
    /// <c>await using</c> is load-bearing: disposing an uncommitted transaction rolls it back, so
    /// every escape path — a Domain guard throwing, a second save failing, cancellation — leaves
    /// no Customer behind. That is a rollback, deliberately not a compensating delete.
    /// </para>
    /// </summary>
    private async Task<Project> CreateCustomerAndProjectAsync(Lead lead, Angebot angebot, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var customer = Customer.Create(lead.Id, lead.Name, lead.Email, lead.Phone, lead.Address);
        await customerRepository.AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var project = await CreateProjectForExistingCustomerAsync(customer, angebot, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return project;
    }

    /// <summary>
    /// An existing Customer is reused exactly as it stands — its details are never refreshed from
    /// the Lead. They were copied when the work was first committed to, and re-reading them here
    /// would let an unrelated Lead edit silently rewrite the party an earlier Project was agreed
    /// with; that is the drift BR-8 forbids for <c>AngebotItem</c>, for the same reason. No
    /// document asks for a refresh.
    ///
    /// <para>
    /// One <c>SaveChangesAsync</c>, no explicit transaction — EF Core's implicit per-save
    /// transaction already covers a single insert. Also reused as the second half of the
    /// create-Customer path, where it runs inside that path's transaction instead.
    /// </para>
    /// </summary>
    private async Task<Project> CreateProjectForExistingCustomerAsync(Customer customer, Angebot angebot, CancellationToken cancellationToken)
    {

        // ERD.md: "AgreedTotal is a snapshot of Angebot.GrossTotal at conversion time". Read once,
        // here, and never re-read: Project holds no reference to the Angebot it came from.
        var project = Project.Create(customer.Id, angebot.Id, angebot.GrossTotal);
        await projectRepository.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return project;
    }
}
