using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.CreateInvoice;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Commands.CreateInvoice;

/// <summary>
/// Orchestration, guard ordering and the BR-3 non-blocking rule. The allocation arithmetic itself
/// is proved in <c>VatAllocationTests</c>; what these prove is that this handler applies it to the
/// right Angebot, rejects the right things, and reserves an invoice number only after every guard
/// that could reject the request has already passed.
/// </summary>
public class CreateInvoiceCommandHandlerTests
{
    private const int AdminId = 2;
    private const int InspectorId = 5;
    private const int ProjectId = 77;

    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeNumberGeneratorService _numberGenerator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CreateInvoiceCommandHandler _handler;

    public CreateInvoiceCommandHandlerTests()
    {
        _handler = new CreateInvoiceCommandHandler(
            new CreateInvoiceCommandValidator(),
            _projectRepository,
            _angebotRepository,
            _invoiceRepository,
            _numberGenerator,
            _unitOfWork,
            _auditService);
    }

    /// <summary>
    /// Builds a real Angebot through its own methods (never a backdoor), then a Project referencing
    /// it. <paramref name="unitPrice"/> at 19% gives a gross of <c>unitPrice × 1.19</c>.
    /// </summary>
    private Project SeedProject(decimal unitPrice = 10_000.00m, VatRate rate = VatRate.Standard)
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00042", InspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(unitPrice), rate);

        return _projectRepository.Seed(
            Project.Create(customerId: 9, angebotId: angebot.Id, agreedTotal: angebot.GrossTotal),
            ProjectId);
    }

    private CreateInvoiceCommand CommandFor(decimal gross) =>
        new(ProjectId, gross, DateTime.UtcNow.AddDays(14), AdminId);

    // ---- Happy path -----------------------------------------------------

    [Fact]
    public async Task AnInvoiceIsCreatedAgainstTheProjectAsADraft()
    {
        SeedProject();

        var result = await _handler.HandleAsync(CommandFor(11_900.00m), CancellationToken.None);

        var invoice = Assert.Single(_invoiceRepository.AddedInvoices);
        Assert.Equal(ProjectId, invoice.ProjectId);
        Assert.Equal(InvoiceStatus.Draft, result.Status);
        Assert.Equal("RE-2026-00001", result.InvoiceNumber);
    }

    /// <summary>
    /// FR-8.2: the split is "consistent with the originating Angebot's rates". A 19% Angebot
    /// invoiced at 11,900.00 yields exactly 10,000.00 net and 1,900.00 VAT.
    /// </summary>
    [Fact]
    public async Task TheAmountsAreSplitFromTheOriginatingAngebotsRateMix()
    {
        SeedProject();

        var result = await _handler.HandleAsync(CommandFor(11_900.00m), CancellationToken.None);

        Assert.Equal(10_000.00m, result.NetAmount);
        Assert.Equal(1_900.00m, result.VatAmount);
        Assert.Equal(11_900.00m, result.GrossAmount);
    }

    [Fact]
    public async Task TheInvoiceIsPersistedThroughOneSaveChanges()
    {
        SeedProject();

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// A single insert is already atomic under EF Core's implicit transaction, so the explicit
    /// boundary D48's amendment added must not be opened here — it would take a lock scope for
    /// nothing (approved Phase 8 decision G-7).
    /// </summary>
    [Fact]
    public async Task NoExplicitTransactionIsOpened()
    {
        SeedProject();

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    [Fact]
    public async Task TheCreationIsAuditedAgainstTheInvoiceAfterTheCommit()
    {
        SeedProject();

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Invoice), entry.EntityType);
        Assert.Equal(AuditAction.InvoiceCreated, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    [Fact]
    public async Task TheNumberIsRequestedForTheCurrentYear()
    {
        SeedProject();

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(DateTime.UtcNow.Year, Assert.Single(_numberGenerator.RequestedYears));
    }

    // ---- BR-3: over-invoicing is allowed --------------------------------

    /// <summary>
    /// <b>BR-3 warns; it does not block.</b> An invoice far beyond the agreed total is a valid
    /// request — the discrepancy surfaces as a negative <c>Remaining</c> on the balance read. If
    /// this test ever fails, someone has turned a documented warning into a prohibition.
    /// </summary>
    [Fact]
    public async Task AnInvoiceExceedingTheAgreedTotalIsAccepted()
    {
        var project = SeedProject();
        var farBeyond = project.AgreedTotal.Amount * 10;

        var result = await _handler.HandleAsync(CommandFor(farBeyond), CancellationToken.None);

        Assert.Equal(farBeyond, result.GrossAmount);
        Assert.Single(_invoiceRepository.AddedInvoices);
    }

    /// <summary>Several invoices may be created against one Project — FR-8.1's whole purpose.</summary>
    [Fact]
    public async Task SeveralInvoicesMayBeCreatedAgainstOneProject()
    {
        SeedProject();

        await _handler.HandleAsync(CommandFor(5_000.00m), CancellationToken.None);
        await _handler.HandleAsync(CommandFor(5_000.00m), CancellationToken.None);
        await _handler.HandleAsync(CommandFor(5_000.00m), CancellationToken.None);

        Assert.Equal(3, _invoiceRepository.AddedInvoices.Count);
    }

    // ---- Guards ---------------------------------------------------------

    [Fact]
    public async Task AnUnknownProjectIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));
    }

    /// <summary>
    /// StateMachine.md §5: "An Invoice cannot exist without an <c>Active</c>/<c>OnHold</c> Project."
    /// </summary>
    [Fact]
    public async Task ACompletedProjectIsRejected()
    {
        var project = SeedProject();
        project.Complete();

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));
    }

    /// <summary>An <c>OnHold</c> Project may still be invoiced — §5 names both states.</summary>
    [Fact]
    public async Task AnOnHoldProjectIsAccepted()
    {
        var project = SeedProject();
        project.PutOnHold();

        var result = await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(100.00m, result.GrossAmount);
    }

    /// <summary>
    /// The approved narrow rule: a positive amount cannot be split across a rate mix with no gross
    /// of its own, so it is refused rather than allocated by an invented rate.
    /// </summary>
    [Fact]
    public async Task APositiveInvoiceAgainstAZeroGrossAngebotIsRejected()
    {
        SeedProject(unitPrice: 0m);

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));
    }

    /// <summary>
    /// ...and the deliberately-preserved companion case: a zero-gross Invoice needs no proportion,
    /// so a zero-gross Angebot must not make it fail. The rule stays as narrow as the arithmetic.
    /// </summary>
    [Fact]
    public async Task AZeroInvoiceAgainstAZeroGrossAngebotIsAllowed()
    {
        SeedProject(unitPrice: 0m);

        var result = await _handler.HandleAsync(CommandFor(0m), CancellationToken.None);

        Assert.Equal(0m, result.GrossAmount);
        Assert.Equal(0m, result.NetAmount);
        Assert.Equal(0m, result.VatAmount);
    }

    [Fact]
    public async Task ANegativeAmountFailsValidation()
    {
        SeedProject();

        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(CommandFor(-1.00m), CancellationToken.None));
    }

    // ---- D66: the number is reserved last -------------------------------

    /// <summary>
    /// <b>A reservation is irreversible</b> — the sequence only ever increments (D52), so a number
    /// taken before a guard rejects the request is a number burned for nothing. Every rejection path
    /// must leave the sequence untouched.
    /// </summary>
    [Fact]
    public async Task NoNumberIsReservedWhenTheProjectDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));

        Assert.Equal(0, _numberGenerator.ReservationCount);
    }

    [Fact]
    public async Task NoNumberIsReservedWhenTheProjectIsCompleted()
    {
        SeedProject().Complete();

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));

        Assert.Equal(0, _numberGenerator.ReservationCount);
    }

    [Fact]
    public async Task NoNumberIsReservedWhenTheAngebotGrossIsZero()
    {
        SeedProject(unitPrice: 0m);

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));

        Assert.Equal(0, _numberGenerator.ReservationCount);
    }

    [Fact]
    public async Task NoNumberIsReservedWhenValidationFails()
    {
        SeedProject();

        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(CommandFor(-1.00m), CancellationToken.None));

        Assert.Equal(0, _numberGenerator.ReservationCount);
    }

    /// <summary>A rejected request must leave nothing behind at all — no row, no commit, no audit.</summary>
    [Fact]
    public async Task ARejectedRequestHasNoSideEffects()
    {
        SeedProject().Complete();

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));

        Assert.Empty(_invoiceRepository.AddedInvoices);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    // ---- Structure -------------------------------------------------------

    /// <summary>
    /// `PermissionMatrix.md` §5 marks "Create Invoice" Admin <c>F</c>, so no ownership rule exists
    /// to enforce — an <c>IOwnershipValidator</c> dependency here would be a semantic error, not
    /// merely redundant (CLAUDE.md §16).
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(CreateInvoiceCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }
}
