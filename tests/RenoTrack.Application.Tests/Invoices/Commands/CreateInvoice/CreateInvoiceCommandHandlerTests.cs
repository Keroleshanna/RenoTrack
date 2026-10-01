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

    // A fixed mid-morning instant, far from any midnight, unless a test moves it (D111 Part 6).
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
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
            _auditService,
            _clock,
            InvoiceCalendar.ForEuropeBerlin());
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

    private const string Description = "Abschlag 1: Malerarbeiten Erdgeschoss";

    private CreateInvoiceCommand CommandFor(
        decimal gross,
        string description = Description,
        DateOnly? servicePeriodStart = null,
        DateOnly? servicePeriodEnd = null) =>
        new(ProjectId, gross, DateTime.UtcNow.AddDays(14), description, servicePeriodStart, servicePeriodEnd, AdminId);

    /// <summary>A real mixed-rate Angebot (BR-6): one 7 % line and one 19 % line.</summary>
    private Project SeedMixedRateProject()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00043", InspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Material", 1m, ItemUnit.Piece(), Money.FromExact(1_000.00m), VatRate.Reduced);
        angebot.AddItemToSection(section, "Arbeit", 1m, ItemUnit.Piece(), Money.FromExact(5_000.00m), VatRate.Standard);

        return _projectRepository.Seed(
            Project.Create(customerId: 9, angebotId: angebot.Id, agreedTotal: angebot.GrossTotal),
            ProjectId);
    }

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

    /// <summary>
    /// D111: the Invoice aggregate keeps one VAT line per rate of the originating Angebot. Billing a
    /// 7 % + 19 % Angebot's whole gross (1,070.00 + 5,950.00) reproduces both rates' figures.
    /// </summary>
    [Fact]
    public async Task AMixedRateAngebotYieldsOneVatLinePerRate()
    {
        SeedMixedRateProject();

        var result = await _handler.HandleAsync(CommandFor(7_020.00m), CancellationToken.None);

        Assert.Collection(
            result.VatLines,
            line =>
            {
                Assert.Equal(VatRate.Reduced, line.Rate);
                Assert.Equal(1_000.00m, line.NetAmount);
                Assert.Equal(70.00m, line.VatAmount);
            },
            line =>
            {
                Assert.Equal(VatRate.Standard, line.Rate);
                Assert.Equal(5_000.00m, line.NetAmount);
                Assert.Equal(950.00m, line.VatAmount);
            });
        Assert.Equal(6_000.00m, result.NetAmount);
        Assert.Equal(1_020.00m, result.VatAmount);
    }

    /// <summary>
    /// The lines on the persisted aggregate are the ones returned — the DTO reads what the Invoice
    /// calculated, and nothing in the command could have supplied them.
    /// </summary>
    [Fact]
    public async Task ThePersistedInvoiceCarriesTheCalculatedLines()
    {
        SeedMixedRateProject();

        await _handler.HandleAsync(CommandFor(3_510.00m), CancellationToken.None);

        var invoice = Assert.Single(_invoiceRepository.AddedInvoices);
        Assert.Equal([VatRate.Reduced, VatRate.Standard], invoice.VatLines.Select(l => l.Rate).ToArray());
        Assert.Equal(invoice.GrossAmount, invoice.NetAmount + invoice.VatAmount);
    }

    [Fact]
    public async Task TheDescriptionAndServicePeriodAreStored()
    {
        SeedProject();

        var result = await _handler.HandleAsync(
            CommandFor(100.00m, "  Malerarbeiten  ", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            CancellationToken.None);

        Assert.Equal("Malerarbeiten", result.Description);
        Assert.Equal(new DateOnly(2026, 9, 1), result.ServicePeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), result.ServicePeriodEnd);
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

        Assert.Equal(2026, Assert.Single(_numberGenerator.RequestedYears));
    }

    // ---- D111 Part 6: one instant, in the company's calendar ---------------

    /// <summary>
    /// 23:30 UTC on 31 December is 00:30 on 1 January in Berlin. The invoice is numbered in the
    /// Berlin year — 2027 — although the UTC year is still 2026. Before this fix it was numbered (and
    /// dated) 2026.
    /// </summary>
    [Fact]
    public async Task AfterBerlinMidnightOnNewYearsEveTheNumberYearIsTheNewYear()
    {
        SeedProject();
        _clock.UtcNow = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(2027, Assert.Single(_numberGenerator.RequestedYears));
    }

    /// <summary>22:59 UTC on 31 December is 23:59 in Berlin — the old year still.</summary>
    [Fact]
    public async Task BeforeBerlinMidnightOnNewYearsEveTheNumberYearIsTheOldYear()
    {
        SeedProject();
        _clock.UtcNow = new DateTimeOffset(2026, 12, 31, 22, 59, 0, TimeSpan.Zero);

        await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(2026, Assert.Single(_numberGenerator.RequestedYears));
    }

    /// <summary>
    /// The number year and the issue date come from ONE clock read, so they cannot disagree: the
    /// stored issue instant is exactly the instant the year was taken from, and the clock is read
    /// exactly once.
    /// </summary>
    [Fact]
    public async Task TheNumberYearAndTheIssueDateComeFromOneInstant()
    {
        SeedProject();
        var instant = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        _clock.UtcNow = instant;

        var result = await _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None);

        Assert.Equal(1, _clock.ReadCount);
        Assert.Equal(instant.UtcDateTime, Assert.Single(_invoiceRepository.AddedInvoices).IssueDate);
        Assert.Equal(instant.UtcDateTime, result.IssueDate);
        Assert.Equal(
            new DateOnly(2027, 1, 1),
            InvoiceCalendar.ForEuropeBerlin().DateOf(result.IssueDate));
        Assert.Equal(2027, Assert.Single(_numberGenerator.RequestedYears));
    }

    /// <summary>
    /// D66 is unchanged: a rejected request reads no clock and reserves no number. The clock is read
    /// after every guard, immediately before the reservation.
    /// </summary>
    [Fact]
    public async Task ARejectedRequestReadsNoClock()
    {
        SeedProject().Complete();

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(CommandFor(100.00m), CancellationToken.None));

        Assert.Equal(0, _clock.ReadCount);
        Assert.Equal(0, _numberGenerator.ReservationCount);
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

    // ---- Description and service period shape (D111) --------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankDescriptionFailsValidationOnTheDescriptionField(string description)
    {
        SeedProject();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(CommandFor(100.00m, description), CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateInvoiceCommand.Description));
    }

    [Fact]
    public async Task ADescriptionOverTheMaximumFailsValidation()
    {
        SeedProject();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            CommandFor(100.00m, new string('a', Invoice.MaxDescriptionLength + 1)), CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateInvoiceCommand.Description));
    }

    /// <summary>The validator measures after trimming, as the Domain does — the two must agree.</summary>
    [Fact]
    public async Task ADescriptionOfExactlyTheMaximumAfterTrimmingIsAccepted()
    {
        SeedProject();

        var result = await _handler.HandleAsync(
            CommandFor(100.00m, " " + new string('a', Invoice.MaxDescriptionLength) + " "),
            CancellationToken.None);

        Assert.Equal(Invoice.MaxDescriptionLength, result.Description.Length);
    }

    [Fact]
    public async Task AServicePeriodEndWithoutAStartFailsValidation()
    {
        SeedProject();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            CommandFor(100.00m, servicePeriodEnd: new DateOnly(2026, 9, 30)), CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateInvoiceCommand.ServicePeriodStart));
    }

    [Fact]
    public async Task AServicePeriodEndingBeforeItStartsFailsValidation()
    {
        SeedProject();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            CommandFor(100.00m, servicePeriodStart: new DateOnly(2026, 9, 30), servicePeriodEnd: new DateOnly(2026, 9, 1)),
            CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateInvoiceCommand.ServicePeriodEnd));
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

    /// <summary>
    /// D111 moved the description and service-period rules into Invoice.Create, which runs after the
    /// reservation. The validator mirrors them so an ordinary bad request still burns no number —
    /// these pin it for every one of the new shape errors.
    /// </summary>
    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("TOO_LONG", null, null)]
    [InlineData("Malerarbeiten", null, "2026-09-30")]
    [InlineData("Malerarbeiten", "2026-09-30", "2026-09-01")]
    public async Task NoNumberIsReservedWhenTheDescriptionOrServicePeriodIsMalformed(
        string description, string? start, string? end)
    {
        SeedProject();
        var text = description == "TOO_LONG" ? new string('a', Invoice.MaxDescriptionLength + 1) : description;

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            CommandFor(
                100.00m,
                text,
                start is null ? null : DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
                end is null ? null : DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)),
            CancellationToken.None));

        Assert.Equal(0, _numberGenerator.ReservationCount);
        Assert.Empty(_invoiceRepository.AddedInvoices);
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

    /// <summary>
    /// D111: the command carries the Admin's gross and nothing else monetary. No net amount, VAT
    /// amount, rate or line can be stated by a caller — the Invoice aggregate calculates them.
    /// </summary>
    [Fact]
    public void TheCommandAcceptsNoNetVatRateOrLine()
    {
        var properties = typeof(CreateInvoiceCommand).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(
            new[]
            {
                nameof(CreateInvoiceCommand.CreatedByAdminId),
                nameof(CreateInvoiceCommand.Description),
                nameof(CreateInvoiceCommand.DueDate),
                nameof(CreateInvoiceCommand.GrossAmount),
                nameof(CreateInvoiceCommand.ProjectId),
                nameof(CreateInvoiceCommand.ServicePeriodEnd),
                nameof(CreateInvoiceCommand.ServicePeriodStart),
            },
            properties);
    }
}
