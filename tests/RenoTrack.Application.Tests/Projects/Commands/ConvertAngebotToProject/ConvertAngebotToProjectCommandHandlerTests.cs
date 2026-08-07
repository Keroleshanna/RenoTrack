using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Projects.Commands.ConvertAngebotToProject;

/// <summary>
/// Orchestration and guard ordering only. **These tests deliberately do not claim to prove
/// atomicity** — a fake <c>IUnitOfWork</c> has no database and rolls nothing back. What they prove
/// is that the handler opens a transaction on the create-Customer path, does not open one when
/// reusing a Customer, and reaches disposal without committing when a save fails. That a rollback
/// actually removes the Customer row is proved in <c>RenoTrack.Infrastructure.Tests</c> against
/// real LocalDB, and only there.
/// </summary>
public class ConvertAngebotToProjectCommandHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int AdminId = 2;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeLeadRepository _leadRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly ConvertAngebotToProjectCommandHandler _handler;

    public ConvertAngebotToProjectCommandHandlerTests()
    {
        _handler = new ConvertAngebotToProjectCommandHandler(
            new ConvertAngebotToProjectCommandValidator(),
            _angebotRepository,
            _leadRepository,
            _customerRepository,
            _projectRepository,
            _unitOfWork,
            _auditService);
    }

    /// <summary>
    /// Drives both aggregates through their own real transitions to the state a customer approval
    /// leaves them in — never a backdoor, so BR-2's precondition is genuinely reached rather than
    /// simulated.
    /// </summary>
    private (Angebot Angebot, Lead Lead) SeedCustomerApproved(
        decimal grossTotal = 100.00m,
        string? address = "Musterstr. 1, 12345 Berlin")
    {
        var lead = _leadRepository.Seed(Lead.Create(
            "M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Website, address));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();

        var angebot = _angebotRepository.Seed(Angebot.Create(lead.Id, null, "ANG-2026-00042", OwningInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(grossTotal), VatRate.Zero);
        angebot.SubmitForReview();
        angebot.Approve(AdminId);
        angebot.Send();
        lead.MarkAngebotSent();
        angebot.RecordCustomerApproval();

        return (angebot, lead);
    }

    private ConvertAngebotToProjectCommand CommandFor(Angebot angebot) => new(angebot.Id, AdminId);

    // ---- Happy path ---------------------------------------------------

    [Fact]
    public async Task AnApprovedAngebotConvertsSuccessfully()
    {
        var (angebot, _) = SeedCustomerApproved();

        var result = await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var project = Assert.Single(_projectRepository.AddedProjects);
        Assert.Equal(project.Id, result.Id);
        Assert.Equal(angebot.Id, result.AngebotId);
        Assert.Equal(ProjectStatus.Active, result.Status);
    }

    /// <summary>
    /// ERD.md: "AgreedTotal is a snapshot of Angebot.GrossTotal at conversion time". A VAT-free
    /// item is used so the expected figure is unambiguous rather than depending on BR-6's
    /// breakdown, which `AngebotTests` already covers exhaustively.
    /// </summary>
    [Fact]
    public async Task AgreedTotalIsASnapshotOfTheAngebotGrossTotal()
    {
        var (angebot, _) = SeedCustomerApproved(grossTotal: 25_673.36m);

        var result = await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(angebot.GrossTotal.Amount, result.AgreedTotal);
        Assert.Equal(25_673.36m, result.AgreedTotal);
    }

    [Fact]
    public async Task ConversionIsAuditedAgainstTheProject()
    {
        var (angebot, _) = SeedCustomerApproved();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Project), entry.EntityType);
        Assert.Equal(AuditAction.ProjectCreated, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    /// <summary>
    /// Sequence Diagram §7's Phase 6 correction: the Lead already reached <c>Won</c> in the
    /// customer's decision handler, so conversion must not touch its status. There is no second
    /// path to <c>Won</c>.
    /// </summary>
    [Fact]
    public async Task TheLeadStatusIsNotTouched()
    {
        var (angebot, lead) = SeedCustomerApproved();
        var statusBefore = lead.Status;

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(statusBefore, lead.Status);
    }

    // ---- Customer find-or-create --------------------------------------

    [Fact]
    public async Task AMissingCustomerIsCreatedFromTheLead()
    {
        var (angebot, lead) = SeedCustomerApproved();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var customer = Assert.Single(_customerRepository.AddedCustomers);
        Assert.Equal(lead.Id, customer.LeadId);
        Assert.Equal(lead.Name, customer.Name);
        Assert.Equal(lead.Email, customer.Email);
        Assert.Equal(lead.Phone, customer.Phone);
        Assert.Equal(lead.Address, customer.Address);
    }

    /// <summary>
    /// The approved nullability decision, reached through the handler rather than only through
    /// <c>Customer.Create</c>: a website Lead has no address, and conversion must still succeed.
    /// </summary>
    [Fact]
    public async Task ALeadWithNoAddressProducesACustomerWithNoAddress()
    {
        var (angebot, _) = SeedCustomerApproved(address: null);

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var customer = Assert.Single(_customerRepository.AddedCustomers);
        Assert.Null(customer.Address);
    }

    [Fact]
    public async Task AnExistingCustomerForTheLeadIsReused()
    {
        var (angebot, lead) = SeedCustomerApproved();
        var existing = _customerRepository.Seed(
            Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567"));

        var result = await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Empty(_customerRepository.AddedCustomers);
        Assert.Equal(existing.Id, result.CustomerId);
    }

    /// <summary>
    /// An existing Customer's copied details are never refreshed from the Lead. Refreshing would
    /// let an unrelated Lead edit rewrite the party an earlier Project was agreed with — the drift
    /// BR-8 forbids for <c>AngebotItem</c>. No document asks for a refresh.
    /// </summary>
    [Fact]
    public async Task AnExistingCustomerDetailsAreNotRefreshedFromTheLead()
    {
        var (angebot, lead) = SeedCustomerApproved();
        var existing = _customerRepository.Seed(
            Customer.Create(lead.Id, "Old Name", "old@example.com", "0000 000", "Old Address 1"));

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal("Old Name", existing.Name);
        Assert.Equal("old@example.com", existing.Email);
        Assert.Equal("0000 000", existing.Phone);
        Assert.Equal("Old Address 1", existing.Address);
    }

    /// <summary>
    /// Customer resolution is by <c>LeadId</c> and nothing else. A different Lead sharing every
    /// contact detail must still get its own Customer — deciding two Leads are the same person is
    /// a customer-identity policy no document states, and `ERD.md` records the consequence as a
    /// known limitation rather than something the handler quietly resolves.
    /// </summary>
    [Fact]
    public async Task ACustomerWithIdenticalDetailsOnAnotherLeadIsNotReused()
    {
        var (angebot, lead) = SeedCustomerApproved();
        _customerRepository.Seed(Customer.Create(
            leadId: lead.Id + 1_000, "M. Klein", "m.klein@example.com", "0176 1234567", "Musterstr. 1, 12345 Berlin"));

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        var created = Assert.Single(_customerRepository.AddedCustomers);
        Assert.Equal(lead.Id, created.LeadId);
    }

    // ---- Guards, all before any side effect ---------------------------

    [Theory]
    [InlineData(AngebotStatus.Draft)]
    [InlineData(AngebotStatus.InReview)]
    [InlineData(AngebotStatus.ChangesRequested)]
    [InlineData(AngebotStatus.ApprovedInternally)]
    [InlineData(AngebotStatus.Sent)]
    [InlineData(AngebotStatus.CustomerRejected)]
    public async Task ANonCustomerApprovedAngebotIsRejected(AngebotStatus status)
    {
        var angebot = _angebotRepository.Seed(SeedAngebotInStatus(status));

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));
    }

    /// <summary>BR-2's rejection must happen before anything is created or saved.</summary>
    [Fact]
    public async Task ANonCustomerApprovedAngebotProducesNoSideEffects()
    {
        var angebot = _angebotRepository.Seed(SeedAngebotInStatus(AngebotStatus.Sent));

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Empty(_customerRepository.AddedCustomers);
        Assert.Empty(_projectRepository.AddedProjects);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task AnAlreadyConvertedAngebotIsRejectedWithNoSideEffects()
    {
        var (angebot, _) = SeedCustomerApproved();
        _projectRepository.SeedConverted(angebot.Id);

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Empty(_customerRepository.AddedCustomers);
        Assert.Empty(_projectRepository.AddedProjects);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    [Fact]
    public async Task AMissingAngebotThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new ConvertAngebotToProjectCommand(9_999, AdminId), CancellationToken.None));
    }

    [Fact]
    public async Task AMissingLeadThrowsNotFound()
    {
        var angebot = _angebotRepository.Seed(SeedAngebotInStatus(AngebotStatus.CustomerApproved, leadId: 4_242));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));
    }

    [Theory]
    [InlineData(0, AdminId)]
    [InlineData(-1, AdminId)]
    [InlineData(1, 0)]
    public async Task AMalformedCommandFailsValidation(int angebotId, int adminId)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new ConvertAngebotToProjectCommand(angebotId, adminId), CancellationToken.None));
    }

    // ---- Transaction orchestration ------------------------------------

    /// <summary>
    /// The create-Customer path saves twice inside one transaction, because
    /// <c>Project.CustomerId</c> needs an identity only the first save produces.
    /// </summary>
    [Fact]
    public async Task CreatingACustomerOpensOneTransactionAndCommitsIt()
    {
        var (angebot, _) = SeedCustomerApproved();

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.BeginTransactionCallCount);
        Assert.Equal(2, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, _unitOfWork.CommitCallCount);
        Assert.Equal(1, _unitOfWork.TransactionDisposeCallCount);
    }

    /// <summary>
    /// Reusing a Customer needs one save, which EF Core's implicit per-save transaction already
    /// makes atomic — opening an explicit one for symmetry would take a lock for nothing.
    /// </summary>
    [Fact]
    public async Task ReusingACustomerOpensNoTransaction()
    {
        var (angebot, lead) = SeedCustomerApproved();
        _customerRepository.Seed(Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567"));

        await _handler.HandleAsync(CommandFor(angebot), CancellationToken.None);

        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.CommitCallCount);
    }

    /// <summary>
    /// When the Project's save fails, the handler must reach transaction disposal without ever
    /// committing — the orchestration half of "no orphaned Customer". That disposal actually
    /// removes the row is proved against real LocalDB in <c>RenoTrack.Infrastructure.Tests</c>;
    /// this fake cannot and does not claim it.
    /// </summary>
    [Fact]
    public async Task AFailedProjectSaveDisposesTheTransactionWithoutCommitting()
    {
        var (angebot, _) = SeedCustomerApproved();
        _unitOfWork.SaveFailure = new InvalidOperationException("database unavailable");
        _unitOfWork.SaveFailureOnCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Equal(1, _unitOfWork.BeginTransactionCallCount);
        Assert.Equal(0, _unitOfWork.CommitCallCount);
        Assert.True(_unitOfWork.TransactionRolledBack);
    }

    [Fact]
    public async Task AFailedConversionIsNotAudited()
    {
        var (angebot, _) = SeedCustomerApproved();
        _unitOfWork.SaveFailure = new InvalidOperationException("database unavailable");
        _unitOfWork.SaveFailureOnCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Empty(_auditService.Calls);
    }

    /// <summary>
    /// <c>Project.Create</c>'s <c>customerId &gt; 0</c> guard is what turns "used an unsaved id"
    /// into an immediate, obvious failure rather than a <c>CustomerId = 0</c> row that fails later
    /// at the foreign key as an unmapped 500. Simulating a repository that does not assign an id
    /// on add — which is what a real one does — proves the guard is load-bearing, not decorative.
    /// </summary>
    [Fact]
    public async Task AnUnsavedCustomerIdIsRefusedByTheDomainGuard()
    {
        var (angebot, _) = SeedCustomerApproved();
        _customerRepository.AssignIdOnAdd = false;

        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(CommandFor(angebot), CancellationToken.None));

        Assert.Empty(_projectRepository.AddedProjects);
        Assert.Equal(0, _unitOfWork.CommitCallCount);
        Assert.True(_unitOfWork.TransactionRolledBack);
    }

    private Angebot SeedAngebotInStatus(AngebotStatus status, int? leadId = null)
    {
        var lead = _leadRepository.Seed(Lead.Create(
            "M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Website));
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        lead.MarkAngebotInProgress();

        var angebot = Angebot.Create(leadId ?? lead.Id, null, $"ANG-2026-{status}", OwningInspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Zero);

        if (status == AngebotStatus.Draft)
        {
            return angebot;
        }

        angebot.SubmitForReview();
        if (status == AngebotStatus.InReview)
        {
            return angebot;
        }

        if (status == AngebotStatus.ChangesRequested)
        {
            angebot.RequestChanges(AdminId);
            return angebot;
        }

        angebot.Approve(AdminId);
        if (status == AngebotStatus.ApprovedInternally)
        {
            return angebot;
        }

        angebot.Send();
        lead.MarkAngebotSent();
        if (status == AngebotStatus.Sent)
        {
            return angebot;
        }

        if (status == AngebotStatus.CustomerRejected)
        {
            angebot.RecordCustomerRejection();
            return angebot;
        }

        angebot.RecordCustomerApproval();
        return angebot;
    }
}
