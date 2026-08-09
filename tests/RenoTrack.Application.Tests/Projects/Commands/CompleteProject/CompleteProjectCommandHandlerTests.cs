using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Projects.Commands.CompleteProject;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Projects.Commands.CompleteProject;

/// <summary>
/// SRS FR-7.3 / FR-8.6, StateMachine.md §4.3 and §5, Sequence Diagram §10.
///
/// <para>
/// The <c>Active</c>-only transition is the aggregate's own and is exercised exhaustively in
/// <c>ProjectTests</c>. What these prove is the half StateMachine.md §5 assigns to this command:
/// the invoice blocking predicate, the override's exact reach, and that an override which overrides
/// nothing leaves no trace.
/// </para>
/// </summary>
public class CompleteProjectCommandHandlerTests
{
    private const int AdminId = 2;
    private const int ProjectId = 77;
    private const string Reason = "Customer waived the final instalment in writing.";

    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CompleteProjectCommandHandler _handler;

    private int _nextInvoiceId = 500;

    public CompleteProjectCommandHandlerTests()
    {
        _handler = new CompleteProjectCommandHandler(
            new CompleteProjectCommandValidator(),
            _projectRepository,
            _invoiceRepository,
            _unitOfWork,
            _auditService);
    }

    private Project SeedProject(ProjectStatus status = ProjectStatus.Active)
    {
        var project = _projectRepository.Seed(
            Project.Create(customerId: 7, angebotId: 42, Money.FromExact(25_673.36m)),
            ProjectId);

        switch (status)
        {
            case ProjectStatus.Active:
                break;
            case ProjectStatus.OnHold:
                project.PutOnHold();
                break;
            case ProjectStatus.Completed:
                project.Complete();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return project;
    }

    /// <summary>Drives a real Invoice to the requested status through its own transitions only.</summary>
    private Invoice SeedInvoice(InvoiceStatus status, int projectId = ProjectId)
    {
        var invoice = _invoiceRepository.Seed(
            Invoice.Create(
                projectId, $"RE-2026-{_nextInvoiceId:00000}", DateTime.UtcNow.AddDays(14),
                Money.FromExact(6_722.69m), Money.FromExact(1_277.31m), Money.FromExact(8_000.00m)),
            _nextInvoiceId++);

        switch (status)
        {
            case InvoiceStatus.Draft:
                break;
            case InvoiceStatus.Sent:
                invoice.Send();
                break;
            case InvoiceStatus.Overdue:
                invoice.Send();
                invoice.MarkOverdue(invoice.DueDate.AddDays(1));
                break;
            case InvoiceStatus.Paid:
                invoice.Send();
                invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, AdminId);
                break;
            case InvoiceStatus.Void:
                invoice.Void("Superseded.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return invoice;
    }

    private Task<Application.Projects.Dtos.ProjectDto> CompleteAsync(bool forceOverride = false, string? reason = null) =>
        _handler.HandleAsync(
            new CompleteProjectCommand(ProjectId, forceOverride, reason, AdminId),
            CancellationToken.None);

    // ---- The happy path ----------------------------------------------------

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task AProjectWhoseInvoicesAreAllSettledCompletes(InvoiceStatus status)
    {
        var project = SeedProject();
        SeedInvoice(status);

        var result = await CompleteAsync();

        Assert.Equal(ProjectStatus.Completed, project.Status);
        Assert.Equal(ProjectStatus.Completed, result.Status);
        Assert.NotNull(project.CompletedAt);
    }

    /// <summary>
    /// K-1: <c>Paid</c> and <c>Void</c> both settle, and a mix of the two is still settled — the
    /// predicate is about every Invoice, not about finding one good one.
    /// </summary>
    [Fact]
    public async Task AMixOfPaidAndVoidInvoicesIsSettled()
    {
        var project = SeedProject();
        SeedInvoice(InvoiceStatus.Paid);
        SeedInvoice(InvoiceStatus.Void);
        SeedInvoice(InvoiceStatus.Paid);

        await CompleteAsync();

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    [Fact]
    public async Task CompletionCommitsExactlyOnceAndOpensNoTransaction()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Paid);

        await CompleteAsync();

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    // ---- The blocking predicate (K-1) --------------------------------------

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task AnUnsettledInvoiceBlocksCompletion(InvoiceStatus status)
    {
        var project = SeedProject();
        SeedInvoice(status);

        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync());

        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    /// <summary>
    /// The reconciliation of StateMachine.md §3.4 against §4.3, made a test rather than a comment:
    /// §3.4 would let a <c>Draft</c> through, §4.3 would not, and K-1 chose §4.3. Deleting this
    /// test is the only way to change that silently.
    /// </summary>
    [Fact]
    public async Task ADraftInvoiceBlocksCompletionEvenThoughStateMachineSection3_4WouldNot()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Paid);
        SeedInvoice(InvoiceStatus.Draft);

        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync());
    }

    /// <summary>
    /// Sequence Diagram §10's "any invoice not Paid" would block on a <c>Void</c>; K-1 chose §4.3,
    /// where <c>Void</c> settles. The mirror of the test above, for the other contradicting source.
    /// </summary>
    [Fact]
    public async Task AVoidInvoiceDoesNotBlockCompletionEvenThoughSequenceSection10Would()
    {
        var project = SeedProject();
        SeedInvoice(InvoiceStatus.Void);

        await CompleteAsync();

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    /// <summary>
    /// I-2. "All Invoices are Paid or Void" is vacuously true over an empty set, so without this
    /// clause a Project that was never invoiced would complete silently. FR-7.3 presupposes a final
    /// invoice exists, so it is blocked and reachable only through the override.
    /// </summary>
    [Fact]
    public async Task AProjectWithNoInvoicesAtAllIsBlocked()
    {
        var project = SeedProject();

        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync());

        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    /// <summary>Another Project's Invoices are not this Project's business.</summary>
    [Fact]
    public async Task InvoicesBelongingToAnotherProjectAreNotConsidered()
    {
        var project = SeedProject();
        SeedInvoice(InvoiceStatus.Paid);
        SeedInvoice(InvoiceStatus.Draft, projectId: ProjectId + 1);

        await CompleteAsync();

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    // ---- The override (FR-8.6) ---------------------------------------------

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task AnOverrideWithAReasonCompletesDespiteUnsettledInvoices(InvoiceStatus status)
    {
        var project = SeedProject();
        SeedInvoice(status);

        await CompleteAsync(forceOverride: true, reason: Reason);

        Assert.Equal(ProjectStatus.Completed, project.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task AnOverrideCompletesAProjectWithNoInvoicesAtAll()
    {
        var project = SeedProject();

        await CompleteAsync(forceOverride: true, reason: Reason);

        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    /// <summary>
    /// I-3. An override must actually override something. Rejected as a 400 (FluentValidation's own
    /// exception, so both of this endpoint's 400s share one body shape), and — the point of the
    /// decision — no audit entry is written, because a recorded "override: &lt;reason&gt;" against a
    /// Project that had nothing to override is a false justification in the permanent record.
    /// </summary>
    [Fact]
    public async Task AnOverrideWithNothingToOverrideIsRejectedAndAuditsNothing()
    {
        var project = SeedProject();
        SeedInvoice(InvoiceStatus.Paid);

        await Assert.ThrowsAsync<ValidationException>(
            () => CompleteAsync(forceOverride: true, reason: Reason));

        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnOverrideWithoutARealReasonIsRejected(string? reason)
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Sent);

        await Assert.ThrowsAsync<ValidationException>(
            () => CompleteAsync(forceOverride: true, reason: reason));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// K-4's mirror rule: a reason supplied without an override is refused rather than dropped. The
    /// caller would otherwise believe they had recorded a justification that went nowhere.
    /// </summary>
    [Fact]
    public async Task AReasonWithoutAnOverrideIsRejectedRatherThanIgnored()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Paid);

        await Assert.ThrowsAsync<ValidationException>(
            () => CompleteAsync(forceOverride: false, reason: Reason));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    // ---- The override's reach (K-5) ----------------------------------------

    /// <summary>
    /// <b>The override bypasses the invoice precondition and nothing else.</b> The Project's own
    /// <c>Active</c>-only invariant belongs to the aggregate and no request field can reach it.
    /// </summary>
    [Theory]
    [InlineData(ProjectStatus.OnHold)]
    [InlineData(ProjectStatus.Completed)]
    public async Task NoOverrideCanCompleteAProjectThatIsNotActive(ProjectStatus status)
    {
        var project = SeedProject(status);
        SeedInvoice(InvoiceStatus.Draft);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CompleteAsync(forceOverride: true, reason: Reason));

        Assert.Equal(status, project.Status);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Audit (K-6) -------------------------------------------------------

    [Fact]
    public async Task ANormalCompletionIsAuditedAgainstTheProjectWithNoDetails()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Paid);

        await CompleteAsync();

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Project), entry.EntityType);
        Assert.Equal(ProjectId, entry.EntityId);
        Assert.Equal(AuditAction.ProjectCompleted, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
        Assert.Null(entry.Details);
    }

    [Fact]
    public async Task AnOverrideCompletionRecordsTheReasonInTheAuditDetails()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Sent);

        await CompleteAsync(forceOverride: true, reason: Reason);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(AuditAction.ProjectCompleted, entry.Action);
        Assert.Equal(Reason, entry.Details);
    }

    /// <summary>
    /// D50: the audit write happens only after the business work is committed. A failing commit
    /// must leave no audit trail claiming the Project was completed.
    /// </summary>
    [Fact]
    public async Task AFailedCommitAuditsNothing()
    {
        SeedProject();
        SeedInvoice(InvoiceStatus.Paid);
        _unitOfWork.SaveFailure = new InvalidOperationException("database unavailable");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CompleteAsync());

        Assert.Empty(_auditService.Calls);
    }

    // ---- Not-found and shape pins ------------------------------------------

    [Fact]
    public async Task AnUnknownProjectThrowsNotFoundBeforeAnyInvoiceIsRead()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CompleteAsync());

        Assert.Equal(0, _invoiceRepository.HasCompletionBlockingInvoicesCallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AMalformedProjectIdFailsValidation(int projectId)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(
                new CompleteProjectCommand(projectId, false, null, AdminId),
                CancellationToken.None));
    }

    /// <summary>
    /// `PermissionMatrix.md` §5 marks this action Admin "F". An <c>IOwnershipValidator</c> on an
    /// "F" action is a semantic error, not merely redundant (CLAUDE.md §16), so its absence is
    /// pinned structurally rather than left to review.
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(CompleteProjectCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }

    /// <summary>
    /// No document describes a notification for project completion — FR-9.1 covers sending an
    /// Angebot or Invoice, FR-9.2's three triggers do not include this, and Sequence Diagram §10
    /// draws no mail participant. Adding one must be a visible signature change reviewed against
    /// those, not a quiet line in the handler.
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoEmailSender()
    {
        var parameterTypes = typeof(CompleteProjectCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IEmailSender), parameterTypes);
    }
}
