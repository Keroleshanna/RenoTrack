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

    // 19% of 6,722.69 rounds to 1,277.31 (BR-11), and the two add back to exactly 8,000.00 — a
    // realistic first-instalment split rather than round numbers that would hide a cent error.
    private static readonly Money ValidNet = Money.FromExact(6_722.69m);
    private static readonly Money ValidVat = Money.FromExact(1_277.31m);
    private static readonly Money ValidGross = Money.FromExact(8_000.00m);

    private static readonly DateTime DueToday = DateTime.UtcNow;

    private static Invoice CreateValid() =>
        Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday, ValidNet, ValidVat, ValidGross);

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
        Assert.Equal(ValidNet, invoice.NetAmount);
        Assert.Equal(ValidVat, invoice.VatAmount);
        Assert.Equal(ValidGross, invoice.GrossAmount);
    }

    /// <summary>
    /// Sequence Diagram §8's request body is <c>{ grossAmount, dueDate }</c> and Wireframe E2
    /// collects exactly those two fields, so the issue date is when the Invoice came into
    /// existence — never a caller's choice.
    /// </summary>
    [Fact]
    public void Create_SetsIssueDateToNow()
    {
        var before = DateTime.UtcNow;

        var invoice = CreateValid();

        Assert.InRange(invoice.IssueDate, before, DateTime.UtcNow);
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
        var invoice = Invoice.Create(
            ValidProjectId, "  RE-2026-00017  ", DueToday, ValidNet, ValidVat, ValidGross);

        Assert.Equal("RE-2026-00017", invoice.InvoiceNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveProjectId(int projectId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Invoice.Create(projectId, ValidInvoiceNumber, DueToday, ValidNet, ValidVat, ValidGross));

        Assert.Equal("projectId", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankInvoiceNumber(string invoiceNumber)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Invoice.Create(ValidProjectId, invoiceNumber, DueToday, ValidNet, ValidVat, ValidGross));

        Assert.Equal("invoiceNumber", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsNullAmounts()
    {
        Assert.Equal("netAmount", Assert.Throws<ArgumentNullException>(
            () => Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday, null!, ValidVat, ValidGross)).ParamName);
        Assert.Equal("vatAmount", Assert.Throws<ArgumentNullException>(
            () => Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday, ValidNet, null!, ValidGross)).ParamName);
        Assert.Equal("grossAmount", Assert.Throws<ArgumentNullException>(
            () => Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday, ValidNet, ValidVat, null!)).ParamName);
    }

    [Fact]
    public void Create_RejectsNegativeAmounts()
    {
        Assert.Equal("netAmount", Assert.Throws<ArgumentException>(
            () => Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday,
                Money.FromExact(-1.00m), Money.Zero, Money.FromExact(-1.00m))).ParamName);

        Assert.Equal("vatAmount", Assert.Throws<ArgumentException>(
            () => Invoice.Create(ValidProjectId, ValidInvoiceNumber, DueToday,
                Money.Zero, Money.FromExact(-1.00m), Money.FromExact(-1.00m))).ParamName);
    }

    /// <summary>
    /// The invariant every VAT allocation (Slice 3) has to satisfy. BR-11 rounds each per-rate
    /// part, and rounded parts do not automatically re-sum to the figure they were split from —
    /// so a lost or invented cent must be impossible to persist, not merely unlikely.
    /// </summary>
    [Theory]
    [InlineData(6_722.69, 1_277.30, 8_000.00)] // one cent short
    [InlineData(6_722.69, 1_277.32, 8_000.00)] // one cent over
    [InlineData(6_722.70, 1_277.31, 8_000.00)] // net drifted
    public void Create_RejectsAmountsThatDoNotAddUp(double net, double vat, double gross)
    {
        var ex = Assert.Throws<ArgumentException>(() => Invoice.Create(
            ValidProjectId,
            ValidInvoiceNumber,
            DueToday,
            Money.FromExact((decimal)net),
            Money.FromExact((decimal)vat),
            Money.FromExact((decimal)gross)));

        Assert.Equal("grossAmount", ex.ParamName);
    }

    /// <summary>
    /// A zero-rated Invoice (VAT 0%) is legal — BR-6 lists 0% among the real rates the company
    /// uses, so requiring a non-zero VAT amount would invent a rule.
    /// </summary>
    [Fact]
    public void Create_AllowsZeroVat()
    {
        var invoice = Invoice.Create(
            ValidProjectId, ValidInvoiceNumber, DueToday,
            Money.FromExact(500.00m), Money.Zero, Money.FromExact(500.00m));

        Assert.Equal(Money.Zero, invoice.VatAmount);
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
        var invoice = Invoice.Create(
            ValidProjectId, ValidInvoiceNumber, DueToday, Money.Zero, Money.Zero, Money.Zero);

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
}
