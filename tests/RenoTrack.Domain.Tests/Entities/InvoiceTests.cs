using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class InvoiceTests
{
    private const int ValidProjectId = 11;
    private const string ValidInvoiceNumber = "RE-2026-00017";
    private const int ValidAdminId = 3;

    private const string ValidDescription = "Abschlag 1: Malerarbeiten Erdgeschoss";

    // Against a single 19 % rate, 8,000.00 gross splits into 6,722.69 net (8,000 / 1.19, rounded per
    // BR-11) and 1,277.31 VAT — a realistic first-instalment split rather than round numbers that
    // would hide a cent error. The Invoice calculates both; the test only states the gross.
    private static readonly Money ValidNet = Money.FromExact(6_722.69m);
    private static readonly Money ValidVat = Money.FromExact(1_277.31m);
    private static readonly Money ValidGross = Money.FromExact(8_000.00m);

    /// <summary>An Angebot whose only rate is 19 % — what Angebot.VatBreakdown hands the handler.</summary>
    private static readonly IReadOnlyList<VatBreakdownLine> StandardRateMix =
        [new VatBreakdownLine(VatRate.Standard, Money.FromExact(1_000.00m), Money.FromExact(190.00m))];

    /// <summary>A real mixed-rate Angebot (BR-6): 0 %, 7 % and 19 % lines together.</summary>
    private static readonly IReadOnlyList<VatBreakdownLine> MixedRateMix =
    [
        new VatBreakdownLine(VatRate.Zero, Money.FromExact(400.00m), Money.Zero),
        new VatBreakdownLine(VatRate.Reduced, Money.FromExact(1_000.00m), Money.FromExact(70.00m)),
        new VatBreakdownLine(VatRate.Standard, Money.FromExact(5_000.00m), Money.FromExact(950.00m)),
    ];

    private static readonly DateTime DueToday = DateTime.UtcNow;

    /// <summary>The server's issue instant — UTC, as the handler reads it from its clock.</summary>
    private static readonly DateTime IssuedAt = new(2026, 10, 1, 8, 15, 0, DateTimeKind.Utc);

    private static Invoice CreateValid() => Create();

    private static Invoice Create(
        Money? gross = null,
        IReadOnlyList<VatBreakdownLine>? rateMix = null,
        string description = ValidDescription,
        DateOnly? servicePeriodStart = null,
        DateOnly? servicePeriodEnd = null,
        int projectId = ValidProjectId,
        string invoiceNumber = ValidInvoiceNumber,
        DateTime? issuedAt = null) =>
        Invoice.Create(
            projectId,
            invoiceNumber,
            issuedAt ?? IssuedAt,
            DueToday,
            gross ?? ValidGross,
            rateMix ?? StandardRateMix,
            description,
            servicePeriodStart,
            servicePeriodEnd);

    /// <summary>
    /// Drives an Invoice to the requested state through its own real transition methods only —
    /// never reflection, never a test-only setter (CLAUDE.md §14). Every state in
    /// <see cref="InvoiceStatus"/> is reachable this way; one that was not would be a dead state.
    /// </summary>
    private static Invoice InState(InvoiceStatus status)
    {
        var invoice = CreateValid();

        switch (status)
        {
            case InvoiceStatus.Draft:
                break;
            case InvoiceStatus.Sent:
                invoice.Send();
                break;
            case InvoiceStatus.Paid:
                invoice.Send();
                invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, ValidAdminId);
                break;
            case InvoiceStatus.Overdue:
                invoice.Send();
                invoice.MarkOverdue(DueToday.AddDays(1));
                break;
            case InvoiceStatus.Void:
                invoice.Void("Issued against the wrong Project.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unhandled InvoiceStatus.");
        }

        Assert.Equal(status, invoice.Status);
        return invoice;
    }

    // ---- Create -------------------------------------------------------

    /// <summary>StateMachine.md §3.2: an Invoice is born <c>Draft</c>; the diagram has no other entry point.</summary>
    [Fact]
    public void Create_InitializesStatusAsDraft()
    {
        Assert.Equal(InvoiceStatus.Draft, CreateValid().Status);
    }

    [Fact]
    public void Create_PreservesProvidedValues()
    {
        var invoice = CreateValid();

        Assert.Equal(ValidProjectId, invoice.ProjectId);
        Assert.Equal(ValidInvoiceNumber, invoice.InvoiceNumber);
        Assert.Equal(DueToday, invoice.DueDate);
        Assert.Equal(ValidGross, invoice.GrossAmount);
        Assert.Equal(ValidDescription, invoice.Description);
        Assert.Null(invoice.ServicePeriodStart);
        Assert.Null(invoice.ServicePeriodEnd);
    }

    /// <summary>
    /// D111 Part 6: the issue date is the server's issue instant, stored exactly as given, so the
    /// handler's single clock read decides both it and the invoice-number year.
    /// </summary>
    [Fact]
    public void Create_StoresTheIssueInstantItIsGiven()
    {
        var issuedAt = new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc);

        var invoice = Create(issuedAt: issuedAt);

        Assert.Equal(issuedAt, invoice.IssueDate);
        Assert.Equal(DateTimeKind.Utc, invoice.IssueDate.Kind);
    }

    /// <summary>
    /// An instant must be UTC. A local or unspecified wall-clock reading would mean a different
    /// moment on every host — and so, at midnight, a different invoice date.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_RejectsANonUtcIssueInstant(DateTimeKind kind)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Create(issuedAt: DateTime.SpecifyKind(new DateTime(2026, 10, 1, 8, 0, 0), kind)));

        Assert.Equal("issuedAt", ex.ParamName);
    }

    [Fact]
    public void Create_LeavesVoidReasonNullAndPaymentsEmpty()
    {
        var invoice = CreateValid();

        Assert.Null(invoice.VoidReason);
        Assert.Empty(invoice.Payments);
    }

    [Fact]
    public void Create_TrimsTheInvoiceNumber()
    {
        var invoice = Create(invoiceNumber: "  RE-2026-00017  ");

        Assert.Equal("RE-2026-00017", invoice.InvoiceNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveProjectId(int projectId)
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(projectId: projectId));

        Assert.Equal("projectId", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankInvoiceNumber(string invoiceNumber)
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(invoiceNumber: invoiceNumber));

        Assert.Equal("invoiceNumber", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        Assert.Equal("grossAmount", Assert.Throws<ArgumentNullException>(() => Invoice.Create(
            ValidProjectId, ValidInvoiceNumber, IssuedAt, DueToday, null!, StandardRateMix, ValidDescription, null, null)).ParamName);
        Assert.Equal("rateMix", Assert.Throws<ArgumentNullException>(() => Invoice.Create(
            ValidProjectId, ValidInvoiceNumber, IssuedAt, DueToday, ValidGross, null!, ValidDescription, null, null)).ParamName);
        Assert.Equal("description", Assert.Throws<ArgumentNullException>(() => Invoice.Create(
            ValidProjectId, ValidInvoiceNumber, IssuedAt, DueToday, ValidGross, StandardRateMix, null!, null, null)).ParamName);
    }

    [Fact]
    public void Create_RejectsANegativeGross()
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(gross: Money.FromExact(-1.00m)));

        Assert.Equal("grossAmount", ex.ParamName);
    }

    // ---- Create: the VAT split (D111) ---------------------------------

    /// <summary>
    /// The Invoice calculates its own split: the caller states only the gross and the Angebot's rate
    /// mix, and net and VAT are derived. 8,000.00 at 19 % is 6,722.69 + 1,277.31 under BR-11.
    /// </summary>
    [Fact]
    public void Create_DerivesNetAndVatFromTheRateMix()
    {
        var invoice = CreateValid();

        Assert.Equal(ValidNet, invoice.NetAmount);
        Assert.Equal(ValidVat, invoice.VatAmount);
        Assert.Equal(ValidGross, invoice.GrossAmount);
    }

    [Fact]
    public void Create_StoresOneVatLinePerRateOfASingleRateMix()
    {
        var line = Assert.Single(CreateValid().VatLines);

        Assert.Equal(VatRate.Standard, line.Rate);
        Assert.Equal(ValidNet, line.NetAmount);
        Assert.Equal(ValidVat, line.VatAmount);
    }

    /// <summary>
    /// BR-6: one Invoice may carry several rates, and FR-8.2 requires the split to follow the
    /// originating Angebot's mix. Every rate present in the mix gets its own line, in rate order.
    /// </summary>
    [Fact]
    public void Create_StoresOneVatLinePerRateOfAMixedRateMix()
    {
        var invoice = Create(gross: Money.FromExact(3_000.00m), rateMix: MixedRateMix);

        Assert.Equal(
            [VatRate.Zero, VatRate.Reduced, VatRate.Standard],
            invoice.VatLines.Select(line => line.Rate).ToArray());
    }

    /// <summary>
    /// The header totals are the lines' own sums — never a second calculation that could disagree
    /// with the lines a document prints.
    /// </summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(1.00)]
    [InlineData(999.99)]
    [InlineData(3_000.00)]
    [InlineData(7_420.00)]
    [InlineData(12_345.67)]
    public void Create_HeaderTotalsAreTheSumsOfTheLines(double gross)
    {
        var invoice = Create(gross: Money.FromExact((decimal)gross), rateMix: MixedRateMix);

        Assert.Equal(Money.Sum(invoice.VatLines.Select(l => l.NetAmount)), invoice.NetAmount);
        Assert.Equal(Money.Sum(invoice.VatLines.Select(l => l.VatAmount)), invoice.VatAmount);
        Assert.Equal(invoice.GrossAmount, invoice.NetAmount + invoice.VatAmount);
    }

    /// <summary>
    /// Billing the Angebot's whole gross reproduces the Angebot's own per-rate figures exactly —
    /// the strongest check that the split follows the originating rates (FR-8.2).
    /// </summary>
    [Fact]
    public void Create_BillingTheWholeAngebotReproducesItsPerRateFigures()
    {
        var invoice = Create(gross: Money.FromExact(7_420.00m), rateMix: MixedRateMix);

        Assert.Equal(
            MixedRateMix.Select(l => (l.Rate, l.NetAmount, l.VatAmount)),
            invoice.VatLines.Select(l => (l.Rate, l.NetAmount, l.VatAmount)));
    }

    /// <summary>
    /// Two lines at one rate would print a VAT summary that counts that rate twice. The real caller
    /// passes Angebot.VatBreakdown, already grouped by rate; any other caller is refused.
    /// </summary>
    [Fact]
    public void Create_RejectsARateMixNamingARateTwice()
    {
        IReadOnlyList<VatBreakdownLine> duplicated =
        [
            new VatBreakdownLine(VatRate.Standard, Money.FromExact(100.00m), Money.FromExact(19.00m)),
            new VatBreakdownLine(VatRate.Standard, Money.FromExact(200.00m), Money.FromExact(38.00m)),
        ];

        var ex = Assert.Throws<ArgumentException>(() => Create(rateMix: duplicated));

        Assert.Equal("rateMix", ex.ParamName);
    }

    /// <summary>
    /// There is no proportion to split a positive gross by when the Angebot totals zero. The handler
    /// refuses this case before reserving a number (D66); reaching here it still fails.
    /// </summary>
    [Fact]
    public void Create_RejectsAPositiveGrossAgainstAZeroTotalRateMix()
    {
        IReadOnlyList<VatBreakdownLine> zeroMix = [new VatBreakdownLine(VatRate.Standard, Money.Zero, Money.Zero)];

        Assert.Throws<ArgumentException>(() => Create(rateMix: zeroMix));
    }

    /// <summary>A zero-gross Invoice has nothing to split, so it carries no lines (and cannot be sent).</summary>
    [Fact]
    public void Create_AZeroGrossInvoiceHasNoVatLines()
    {
        var invoice = Create(gross: Money.Zero, rateMix: MixedRateMix);

        Assert.Empty(invoice.VatLines);
        Assert.Equal(Money.Zero, invoice.NetAmount);
        Assert.Equal(Money.Zero, invoice.VatAmount);
    }

    /// <summary>
    /// A zero-rated Invoice (VAT 0%) is legal — BR-6 lists 0% among the real rates the company
    /// uses, so requiring a non-zero VAT amount would invent a rule.
    /// </summary>
    [Fact]
    public void Create_AllowsZeroVat()
    {
        IReadOnlyList<VatBreakdownLine> zeroRated = [new VatBreakdownLine(VatRate.Zero, Money.FromExact(500.00m), Money.Zero)];

        var invoice = Create(gross: Money.FromExact(500.00m), rateMix: zeroRated);

        Assert.Equal(Money.Zero, invoice.VatAmount);
        Assert.Equal(Money.FromExact(500.00m), invoice.NetAmount);
    }

    /// <summary>
    /// D111: no caller can state a net amount, a VAT amount or a line. The gross — the Admin's
    /// choice of how much this invoice bills (FR-8.1) — is the only money <c>Create</c> accepts.
    /// </summary>
    [Fact]
    public void Create_AcceptsNoNetVatOrLineParameter()
    {
        var parameters = typeof(Invoice)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(Invoice.Create))
            .SelectMany(m => m.GetParameters())
            .ToArray();

        var moneyParameters = parameters
            .Where(p => p.ParameterType == typeof(Money))
            .Select(p => p.Name)
            .ToArray();
        Assert.Equal(new[] { "grossAmount" }, moneyParameters);

        Assert.DoesNotContain(parameters, p =>
            p.ParameterType == typeof(InvoiceVatLine)
            || p.ParameterType.GenericTypeArguments.Contains(typeof(InvoiceVatLine)));
    }

    // ---- Create: description and service period (D111) -----------------

    [Fact]
    public void Create_TrimsTheDescription()
    {
        var invoice = Create(description: "  Malerarbeiten  ");

        Assert.Equal("Malerarbeiten", invoice.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsABlankDescription(string description)
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(description: description));

        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void Create_AcceptsADescriptionOfExactlyTheMaximumLength()
    {
        var invoice = Create(description: new string('a', Invoice.MaxDescriptionLength));

        Assert.Equal(Invoice.MaxDescriptionLength, invoice.Description.Length);
    }

    [Fact]
    public void Create_RejectsADescriptionOverTheMaximumLength()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Create(description: new string('a', Invoice.MaxDescriptionLength + 1)));

        Assert.Equal("description", ex.ParamName);
    }

    /// <summary>The length is measured after trimming, because the trimmed text is what is stored.</summary>
    [Fact]
    public void Create_MeasuresTheDescriptionAfterTrimming()
    {
        var invoice = Create(description: "  " + new string('a', Invoice.MaxDescriptionLength) + "  ");

        Assert.Equal(Invoice.MaxDescriptionLength, invoice.Description.Length);
    }

    [Fact]
    public void Invoice_MaxDescriptionLength_Is500()
    {
        Assert.Equal(500, Invoice.MaxDescriptionLength);
    }

    [Fact]
    public void Create_StoresAServicePeriod()
    {
        var invoice = Create(servicePeriodStart: new DateOnly(2026, 9, 1), servicePeriodEnd: new DateOnly(2026, 9, 30));

        Assert.Equal(new DateOnly(2026, 9, 1), invoice.ServicePeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), invoice.ServicePeriodEnd);
    }

    /// <summary>A single service date is a start with no end.</summary>
    [Fact]
    public void Create_StoresASingleServiceDate()
    {
        var invoice = Create(servicePeriodStart: new DateOnly(2026, 9, 15));

        Assert.Equal(new DateOnly(2026, 9, 15), invoice.ServicePeriodStart);
        Assert.Null(invoice.ServicePeriodEnd);
    }

    [Fact]
    public void Create_AcceptsAPeriodStartingAndEndingOnOneDay()
    {
        var day = new DateOnly(2026, 9, 15);

        var invoice = Create(servicePeriodStart: day, servicePeriodEnd: day);

        Assert.Equal(day, invoice.ServicePeriodEnd);
    }

    [Fact]
    public void Create_RejectsAServicePeriodEndWithoutAStart()
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(servicePeriodEnd: new DateOnly(2026, 9, 30)));

        Assert.Equal("servicePeriodEnd", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsAServicePeriodEndingBeforeItStarts()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Create(servicePeriodStart: new DateOnly(2026, 9, 30), servicePeriodEnd: new DateOnly(2026, 9, 1)));

        Assert.Equal("servicePeriodEnd", ex.ParamName);
    }

    // ---- Send ---------------------------------------------------------

    [Fact]
    public void Send_FromDraft_MovesToSent()
    {
        var invoice = InState(InvoiceStatus.Draft);

        invoice.Send();

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Void)]
    public void Send_FromAnyOtherState_Throws(InvoiceStatus status)
    {
        var invoice = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(invoice.Send);

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Contains(nameof(InvoiceStatus.Draft), ex.Message);
    }

    /// <summary>StateMachine.md §3.3's <c>Draft → Sent</c> guard: "Invoice has a valid GrossAmount &gt; 0".</summary>
    [Fact]
    public void Send_RejectsAZeroGrossInvoice()
    {
        var invoice = Create(gross: Money.Zero);

        var ex = Assert.Throws<InvalidOperationException>(invoice.Send);

        Assert.Contains("greater than zero", ex.Message);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    /// <summary>
    /// ERD.md's <c>Invoices</c> table has no <c>SentAt</c> column, unlike <c>Angebote</c>. The
    /// asymmetry belongs to the documents; adding a property to remove it would invent schema.
    /// </summary>
    [Fact]
    public void Invoice_HasNoSentAtProperty()
    {
        Assert.Null(typeof(Invoice).GetProperty("SentAt"));
    }

    // ---- MarkOverdue --------------------------------------------------

    [Fact]
    public void MarkOverdue_FromSentAndPastDue_MovesToOverdue()
    {
        var invoice = InState(InvoiceStatus.Sent);

        invoice.MarkOverdue(DueToday.AddDays(1));

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
    }

    /// <summary>
    /// StateMachine.md §3.2 draws exactly one edge into <c>Overdue</c>, from <c>Sent</c>. The
    /// "and not yet Paid" half of §3.3's guard is what restricting the source state expresses.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Void)]
    public void MarkOverdue_FromAnyOtherState_Throws(InvoiceStatus status)
    {
        var invoice = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(() => invoice.MarkOverdue(DueToday.AddDays(1)));

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Contains(nameof(InvoiceStatus.Sent), ex.Message);
    }

    /// <summary>§3.3 says "DueDate &lt; today" — an invoice due today is not overdue today.</summary>
    [Fact]
    public void MarkOverdue_OnTheDueDateItself_Throws()
    {
        var invoice = InState(InvoiceStatus.Sent);

        Assert.Throws<InvalidOperationException>(() => invoice.MarkOverdue(DueToday));

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    [Fact]
    public void MarkOverdue_BeforeTheDueDate_Throws()
    {
        var invoice = InState(InvoiceStatus.Sent);

        Assert.Throws<InvalidOperationException>(() => invoice.MarkOverdue(DueToday.AddDays(-5)));

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    // ---- MarkPaid -----------------------------------------------------

    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public void MarkPaid_FromSentOrOverdue_MovesToPaid(InvoiceStatus status)
    {
        var invoice = InState(status);

        invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, ValidAdminId);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public void MarkPaid_FromAnyOtherState_Throws(InvoiceStatus status)
    {
        var invoice = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(
            () => invoice.MarkPaid(PaymentMethod.Cash, DateTime.UtcNow, ValidAdminId));

        Assert.Contains(status.ToString(), ex.Message);
    }

    [Fact]
    public void MarkPaid_RecordsThePaymentAgainstTheInvoice()
    {
        var invoice = InState(InvoiceStatus.Sent);
        var paidAt = new DateTime(2026, 8, 15, 9, 30, 0, DateTimeKind.Utc);

        var payment = invoice.MarkPaid(PaymentMethod.Cash, paidAt, ValidAdminId);

        Assert.Single(invoice.Payments);
        Assert.Same(payment, invoice.Payments[0]);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(paidAt, payment.PaidAt);
        Assert.Equal(ValidAdminId, payment.RecordedByAdminId);
    }

    /// <summary>
    /// <b>Phase 8 is full-payment-only, pinned here deliberately.</b> Neither FR-8.4, nor Sequence
    /// Diagram §9's <c>{ paidAt, method }</c> body, nor Wireframe E3 offers an amount to supply, so
    /// the Payment always carries the Invoice's own gross. ERD.md's one-to-many Payments shape is
    /// forward-compatibility for a partial-payment capability that does not exist yet — this test
    /// exists so the schema can never be mistaken for the semantics.
    /// </summary>
    [Fact]
    public void MarkPaid_AlwaysRecordsTheFullGrossAmount()
    {
        var invoice = InState(InvoiceStatus.Sent);

        var payment = invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, ValidAdminId);

        Assert.Equal(invoice.GrossAmount, payment.Amount);
    }

    /// <summary>
    /// There is no overload, no optional parameter and no other public path by which a caller could
    /// supply a payment amount — partial payment is absent by construction, not by convention.
    /// </summary>
    [Fact]
    public void MarkPaid_AcceptsNoAmountParameter()
    {
        var parameters = typeof(Invoice)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(Invoice.MarkPaid))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(Money), parameters);
    }

    [Fact]
    public void MarkPaid_RejectsANonPositiveRecordedByAdminId()
    {
        var invoice = InState(InvoiceStatus.Sent);

        var ex = Assert.Throws<ArgumentException>(
            () => invoice.MarkPaid(PaymentMethod.Other, DateTime.UtcNow, 0));

        Assert.Equal("recordedByAdminId", ex.ParamName);
    }

    /// <summary>A rejected payment must leave no residue — neither a status change nor a child row.</summary>
    [Fact]
    public void MarkPaid_WhenRejected_AddsNoPaymentAndLeavesStatusUntouched()
    {
        var invoice = InState(InvoiceStatus.Sent);

        Assert.Throws<ArgumentException>(() => invoice.MarkPaid(PaymentMethod.Other, DateTime.UtcNow, -1));

        Assert.Empty(invoice.Payments);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    // ---- Void ---------------------------------------------------------

    /// <summary>
    /// StateMachine.md §3.3 permits <c>Draft</c>, <c>Sent</c> and <c>Overdue</c> to be voided.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    public void Void_FromDraftSentOrOverdue_MovesToVoid(InvoiceStatus status)
    {
        var invoice = InState(status);

        invoice.Void("Duplicate of RE-2026-00016.");

        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Equal("Duplicate of RE-2026-00016.", invoice.VoidReason);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public void Void_FromATerminalState_Throws(InvoiceStatus status)
    {
        var invoice = InState(status);

        var ex = Assert.Throws<InvalidOperationException>(() => invoice.Void("Too late."));

        Assert.Contains(status.ToString(), ex.Message);
    }

    /// <summary>
    /// PermissionMatrix.md §5 requires a reason without qualification. StateMachine.md §3.3's
    /// <c>Draft → Void</c> row leaves its guard cell blank where the <c>Sent</c>/<c>Overdue</c> row
    /// says "Admin provides a reason" — treated as an omission in that table, not an exemption, and
    /// reconciled there.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Void_RejectsABlankReasonFromEveryVoidableState(string reason)
    {
        foreach (var status in new[] { InvoiceStatus.Draft, InvoiceStatus.Sent, InvoiceStatus.Overdue })
        {
            var invoice = InState(status);

            var ex = Assert.Throws<ArgumentException>(() => invoice.Void(reason));

            Assert.Equal("reason", ex.ParamName);
            Assert.Equal(status, invoice.Status);
            Assert.Null(invoice.VoidReason);
        }
    }

    [Fact]
    public void Void_TrimsTheReason()
    {
        var invoice = InState(InvoiceStatus.Draft);

        invoice.Void("  Customer cancelled.  ");

        Assert.Equal("Customer cancelled.", invoice.VoidReason);
    }

    /// <summary>
    /// BR-9: "An Invoice number, once issued, is never reused or reassigned — even if that Invoice
    /// is later Voided." Voiding preserves the number, and nothing anywhere can change it.
    /// </summary>
    [Fact]
    public void Void_KeepsTheInvoiceNumber()
    {
        var invoice = InState(InvoiceStatus.Sent);

        invoice.Void("Wrong amount.");

        Assert.Equal(ValidInvoiceNumber, invoice.InvoiceNumber);
    }

    // ---- Terminal states ----------------------------------------------

    /// <summary>StateMachine.md §3.2 gives <c>Paid</c> and <c>Void</c> no outgoing edge at all.</summary>
    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public void TerminalStates_RefuseEveryTransition(InvoiceStatus status)
    {
        var invoice = InState(status);

        Assert.Throws<InvalidOperationException>(invoice.Send);
        Assert.Throws<InvalidOperationException>(() => invoice.MarkOverdue(DueToday.AddDays(30)));
        Assert.Throws<InvalidOperationException>(
            () => invoice.MarkPaid(PaymentMethod.Cash, DateTime.UtcNow, ValidAdminId));
        Assert.Throws<InvalidOperationException>(() => invoice.Void("No."));

        Assert.Equal(status, invoice.Status);
    }

    /// <summary>
    /// The amounts are fixed at creation — no transition may move them, which is what makes an
    /// Invoice a record rather than a working document. The same structural guarantee
    /// <c>Project.AgreedTotal</c> has.
    /// </summary>
    [Fact]
    public void Amounts_SurviveEveryTransition()
    {
        var invoice = CreateValid();

        invoice.Send();
        Assert.Equal(ValidGross, invoice.GrossAmount);

        invoice.MarkOverdue(DueToday.AddDays(1));
        Assert.Equal(ValidGross, invoice.GrossAmount);

        invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, ValidAdminId);
        Assert.Equal(ValidNet, invoice.NetAmount);
        Assert.Equal(ValidVat, invoice.VatAmount);
        Assert.Equal(ValidGross, invoice.GrossAmount);
        Assert.Equal(ValidNet, Assert.Single(invoice.VatLines).NetAmount);
        Assert.Equal(ValidDescription, invoice.Description);
    }

    // ---- Structure ----------------------------------------------------

    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(Invoice)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    /// <summary>
    /// StateMachine.md §3.1 and ERD.md's <c>Invoices.Status</c> column both define exactly these
    /// five. A sixth appearing here would be a state nobody modelled a transition for.
    /// </summary>
    [Fact]
    public void InvoiceStatus_HasExactlyTheFiveDocumentedStates()
    {
        Assert.Equal(
            [
                nameof(InvoiceStatus.Draft),
                nameof(InvoiceStatus.Overdue),
                nameof(InvoiceStatus.Paid),
                nameof(InvoiceStatus.Sent),
                nameof(InvoiceStatus.Void),
            ],
            Enum.GetNames<InvoiceStatus>().OrderBy(n => n).ToArray());
    }

    [Fact]
    public void ExposesNoPublicSetters()
    {
        var settableProperties = typeof(Invoice)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToArray();

        Assert.Empty(settableProperties);
    }

    /// <summary>
    /// CLAUDE.md §2: independent aggregates relate by id only. An Invoice cannot see the Project it
    /// bills, which is why StateMachine.md §5's "an Invoice cannot exist without an Active/OnHold
    /// Project" is enforced by <c>CreateInvoiceCommand</c> rather than here.
    /// </summary>
    [Theory]
    [InlineData(typeof(Project))]
    [InlineData(typeof(Angebot))]
    [InlineData(typeof(Customer))]
    [InlineData(typeof(Lead))]
    [InlineData(typeof(TokenLink))]
    public void HasNoReferenceToOtherAggregatesAsTypes(Type foreignAggregate)
    {
        var referencedTypes = typeof(Invoice)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(typeof(Invoice)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.FieldType))
            .SelectMany(t => new[] { t }.Concat(t.GenericTypeArguments));

        Assert.DoesNotContain(foreignAggregate, referencedTypes);
    }

    /// <summary>
    /// Every state change is a named transition (CLAUDE.md §2) — no <c>SetStatus</c>-shaped escape
    /// hatch, and the mutating surface is exactly the four transitions StateMachine.md §3.3 defines.
    /// </summary>
    [Fact]
    public void ExposesExactlyTheDocumentedTransitions()
    {
        var publicMethods = typeof(Invoice)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            [
                nameof(Invoice.MarkOverdue),
                nameof(Invoice.MarkPaid),
                nameof(Invoice.Send),
                nameof(Invoice.Void),
            ],
            publicMethods);
    }

    /// <summary>
    /// Payments is exposed as <see cref="IReadOnlyList{T}"/> over a private backing field, with no
    /// setter — the same shape <c>Inspection.Photos</c> and <c>Angebot.Sections</c> use, so
    /// <see cref="Invoice.MarkPaid"/> is the only way a payment enters the aggregate.
    /// </summary>
    [Fact]
    public void PaymentsCollection_IsExposedReadOnlyWithNoSetter()
    {
        var payments = typeof(Invoice).GetProperty(nameof(Invoice.Payments))!;

        Assert.Equal(typeof(IReadOnlyList<Payment>), payments.PropertyType);
        Assert.Null(payments.SetMethod);
    }

    /// <summary>
    /// VatLines has the same shape, so <see cref="Invoice.Create"/> is the only way a line enters —
    /// and nothing can add, remove or replace one afterwards.
    /// </summary>
    [Fact]
    public void VatLinesCollection_IsExposedReadOnlyWithNoSetter()
    {
        var vatLines = typeof(Invoice).GetProperty(nameof(Invoice.VatLines))!;

        Assert.Equal(typeof(IReadOnlyList<InvoiceVatLine>), vatLines.PropertyType);
        Assert.Null(vatLines.SetMethod);
    }

    /// <summary>A line is created only by Invoice.Create — never by a caller.</summary>
    [Fact]
    public void InvoiceVatLine_HasNoPublicConstructorAndNoPublicSetters()
    {
        Assert.Empty(typeof(InvoiceVatLine).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            typeof(InvoiceVatLine).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.SetMethod is { IsPublic: true });
    }

    [Fact]
    public void InvoiceVatLine_RejectsNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(
            () => new InvoiceVatLine(VatRate.Standard, Money.FromExact(-0.01m), Money.Zero));
        Assert.Throws<ArgumentException>(
            () => new InvoiceVatLine(VatRate.Standard, Money.Zero, Money.FromExact(-0.01m)));
    }
}
