using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A formal bill issued against a Project (SRS §3.8, StateMachine.md §3). Aggregate root
/// (Architecture.md §6) → <see cref="Payment"/> (child). References <see cref="ProjectId"/> by id
/// only, with no navigation property, so this type has zero compile-time knowledge of Project,
/// Angebot or Customer — the same shape every other aggregate root here has.
///
/// <para>
/// <b><see cref="InvoiceNumber"/> is generated externally</b> (Architecture.md §8's
/// <c>INumberGeneratorService</c>) and passed in already formed, exactly as
/// <c>Angebot.AngebotNumber</c> is. BR-9 — a number is never reused, and a Void invoice keeps its
/// number — is upheld structurally: nothing here can change or clear the number, and
/// <see cref="Void"/> is a status change rather than a deletion.
/// </para>
/// <para>
/// <b>This aggregate calculates its own VAT split (Phase 14 Slice 2, D111).</b> SRS FR-8.2 requires
/// a VAT breakdown "consistent with the originating Angebot's rates". The Admin chooses only how
/// much this invoice bills (<see cref="GrossAmount"/>, FR-8.1's splitting); the caller passes the
/// originating Angebot's rate mix as plain values, and <see cref="Create"/> splits the gross across
/// it with <see cref="VatAllocation"/>. No caller can supply a net amount, a VAT amount, a rate or a
/// line. Reading the Angebot remains the Application layer's job (CLAUDE.md §2 — this type cannot
/// load another aggregate); deciding what the Invoice charges at each rate is this one's.
/// </para>
/// <para>
/// <b><see cref="VatLines"/> are stored, and <see cref="NetAmount"/>/<see cref="VatAmount"/> are
/// their sums.</b> The Invoice document must state "applicable VAT rate(s) and VAT amount(s)"
/// (BR-5), and one Invoice may carry several rates (BR-6). Phase 8 computed this split and
/// discarded it; it is kept now. A line is a calculated result, not ERD.md's deferred
/// <c>InvoiceLine</c>: it has no description, quantity or price. The header totals stay stored
/// columns, like <c>Angebot.NetTotal</c>, and cannot drift because nothing changes the lines after
/// creation.
/// </para>
/// <para>
/// <b><see cref="Description"/> and the optional service period are the Admin's</b>, set once at
/// creation. There is no edit: an issued invoice is corrected by voiding it and issuing another
/// (BR-9).
/// </para>
/// <para>
/// <b>There is no <c>SentAt</c>.</b> ERD.md's <c>Invoices</c> table defines none (unlike
/// <c>Angebote</c>), and adding one would be inventing schema.
/// </para>
/// </summary>
public sealed class Invoice
{
    /// <summary>
    /// The maximum length of <see cref="Description"/>, read by the Application validator, the
    /// guard in <see cref="Create"/> and the EF configuration alike — one definition, so the three
    /// cannot drift (D109's pattern, D111).
    /// </summary>
    public const int MaxDescriptionLength = 500;

    private readonly List<Payment> _payments = [];
    private readonly List<InvoiceVatLine> _vatLines = [];

    public int Id { get; private set; }
    public int ProjectId { get; private set; }
    public string InvoiceNumber { get; private set; }
    public DateTime IssueDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public Money NetAmount { get; private set; }
    public Money VatAmount { get; private set; }
    public Money GrossAmount { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>
    /// What this invoice bills for, in the Admin's words. Required for every invoice created from
    /// Slice 2 on. A row created before then holds an empty string — it was never asked for one,
    /// and none is invented for it; the document assembler refuses such an invoice instead.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>The first day of the service period, when the Admin gave one. Never assumed.</summary>
    public DateOnly? ServicePeriodStart { get; private set; }

    /// <summary>The last day of the service period; only ever present together with a start.</summary>
    public DateOnly? ServicePeriodEnd { get; private set; }

    public IReadOnlyList<Payment> Payments => _payments;

    /// <summary>
    /// One line per VAT rate this invoice bills at, ordered by rate, calculated by
    /// <see cref="Create"/>. Empty for a zero-gross invoice, and for a row created before Slice 2.
    /// </summary>
    public IReadOnlyList<InvoiceVatLine> VatLines => _vatLines;

    /// <summary>
    /// Assignment only — every guard lives in <see cref="Create"/> (CLAUDE.md §2), so nothing
    /// re-runs when EF Core materialises a persisted row through this same private constructor.
    /// That matters concretely here: a row created before Slice 2 has an empty
    /// <see cref="Description"/>, which <see cref="Create"/> would refuse, and must still load.
    /// </summary>
    private Invoice(
        int projectId,
        string invoiceNumber,
        DateTime issueDate,
        DateTime dueDate,
        Money netAmount,
        Money vatAmount,
        Money grossAmount,
        string description,
        DateOnly? servicePeriodStart,
        DateOnly? servicePeriodEnd)
    {
        ProjectId = projectId;
        InvoiceNumber = invoiceNumber;
        IssueDate = issueDate;
        DueDate = dueDate;
        NetAmount = netAmount;
        VatAmount = vatAmount;
        GrossAmount = grossAmount;
        Description = description;
        ServicePeriodStart = servicePeriodStart;
        ServicePeriodEnd = servicePeriodEnd;
        Status = InvoiceStatus.Draft;
        VoidReason = null;
    }

    /// <summary>
    /// StateMachine.md §3.2: an Invoice is born <c>Draft</c> — the diagram has no other entry
    /// point. Sequence Diagram §8 is the flow.
    ///
    /// <para>
    /// <b>The VAT split is calculated here.</b> <paramref name="grossAmount"/> is the Admin's choice
    /// of how much this invoice bills (FR-8.1); <paramref name="rateMix"/> is the originating
    /// Angebot's per-rate breakdown, passed as values. <see cref="VatAllocation.ProportionalTo"/>
    /// splits the gross across the mix and the resulting lines become <see cref="VatLines"/>; the
    /// header net and VAT are their sums. <c>Net + VAT == Gross</c> therefore holds by construction,
    /// and is still checked as a backstop.
    /// </para>
    /// <para>
    /// <b><see cref="IssueDate"/> is the server's issue instant, in UTC, passed as
    /// <paramref name="issuedAt"/> — never a request field and never a client's choice</b>
    /// (D111 Part 6). It used to be read here from <see cref="DateTime.UtcNow"/>. It became a parameter
    /// so that the handler can read the clock exactly once and use that one instant for both the
    /// invoice-number year and this date: two reads could straddle midnight on New Year's Eve and
    /// number an invoice in one year while dating it in the next. It is stored as given; the calendar
    /// date printed on the invoice is derived from it in the company's zone, not here.
    /// </para>
    /// <para>
    /// The guards are self-guards only. That <paramref name="projectId"/> names a real Project in an
    /// <c>Active</c>/<c>OnHold</c> state is checked by <c>CreateInvoiceCommand</c> (StateMachine.md
    /// §5), and so is the case of a positive gross against a zero-total Angebot — there, before the
    /// invoice number is reserved (D66). The same case reaching here still fails, as a backstop.
    /// </para>
    /// <para>
    /// <b><paramref name="dueDate"/> is not constrained</b>, and neither is the service period
    /// against the issue or due date. No requirement document places a rule on either; the only
    /// period rules are the shape ones below.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">The gross, the rate mix or the description is null.</exception>
    /// <exception cref="ArgumentException">
    /// The project id is not positive, the invoice number or description is blank, the issue instant
    /// is not UTC, the description is too long, the gross is negative, the rate mix names a rate twice or cannot carry a positive
    /// gross, the service period has an end without a start, or ends before it starts.
    /// </exception>
    public static Invoice Create(
        int projectId,
        string invoiceNumber,
        DateTime issuedAt,
        DateTime dueDate,
        Money grossAmount,
        IReadOnlyList<VatBreakdownLine> rateMix,
        string description,
        DateOnly? servicePeriodStart,
        DateOnly? servicePeriodEnd)
    {
        if (projectId <= 0)
            throw new ArgumentException("Project id must be positive.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));

        // An instant, not a wall-clock reading: anything but UTC would be interpreted differently on
        // every host. A lifetime invariant of the value as given, so it belongs here in the factory;
        // a row read back from datetime2 is Unspecified and must still load (CLAUDE.md §2).
        if (issuedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The issue instant must be UTC.", nameof(issuedAt));

        ArgumentNullException.ThrowIfNull(grossAmount);
        ArgumentNullException.ThrowIfNull(rateMix);
        ArgumentNullException.ThrowIfNull(description);

        if (grossAmount.Amount < 0)
            throw new ArgumentException("Gross amount cannot be negative.", nameof(grossAmount));

        // Two lines at one rate would print a VAT summary that counts that rate twice. The real
        // caller passes Angebot.VatBreakdown, which is grouped by rate, so this cannot happen through
        // it — the guard is for any other caller, and the database index is the backstop below this.
        if (rateMix.Select(line => line.Rate).Distinct().Count() != rateMix.Count)
            throw new ArgumentException("The VAT rate mix names a rate more than once.", nameof(rateMix));

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length == 0)
            throw new ArgumentException("A description is required.", nameof(description));
        if (trimmedDescription.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"A description may be at most {MaxDescriptionLength} characters.", nameof(description));
        }

        if (servicePeriodEnd is not null && servicePeriodStart is null)
        {
            throw new ArgumentException(
                "A service period end requires a start.", nameof(servicePeriodEnd));
        }
        if (servicePeriodEnd < servicePeriodStart)
        {
            throw new ArgumentException(
                "A service period cannot end before it starts.", nameof(servicePeriodEnd));
        }

        var allocation = VatAllocation.ProportionalTo(rateMix, grossAmount);

        // The invariant every VAT allocation must satisfy. VatAllocation guarantees it and the
        // totals are the lines' own sums, so this cannot fail today; it stays so that a future
        // change to the allocation which loses or invents a cent can never reach the database.
        if (allocation.NetAmount + allocation.VatAmount != grossAmount)
        {
            throw new ArgumentException(
                $"Net ({allocation.NetAmount.Amount}) plus VAT ({allocation.VatAmount.Amount}) must equal gross ({grossAmount.Amount}).",
                nameof(grossAmount));
        }

        var invoice = new Invoice(
            projectId,
            invoiceNumber.Trim(),
            issuedAt,
            dueDate,
            allocation.NetAmount,
            allocation.VatAmount,
            grossAmount,
            trimmedDescription,
            servicePeriodStart,
            servicePeriodEnd);

        invoice._vatLines.AddRange(
            allocation.Lines.Select(line => new InvoiceVatLine(line.Rate, line.NetAmount, line.VatAmount)));

        return invoice;
    }

    /// <summary>
    /// StateMachine.md §3.3: <c>Draft → Sent</c>, guarded on "Invoice has a valid GrossAmount &gt; 0".
    /// The token link, the email and (from Phase 14) the PDF are all the Application layer's work
    /// afterwards — this aggregate has no knowledge of any of them, exactly as <c>Angebot.Send</c>
    /// has none.
    ///
    /// <para>
    /// <b>No <c>SentAt</c> is recorded</b>, because ERD.md's <c>Invoices</c> table has no such
    /// column. <c>Angebot</c> has one and this does not; that asymmetry is the documents', and
    /// adding a column here to remove it would be inventing schema.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The Invoice is not <c>Draft</c>, or its gross amount is zero.</exception>
    public void Send()
    {
        EnsureStatus(nameof(Send), InvoiceStatus.Draft);

        if (GrossAmount.Amount <= 0)
        {
            throw new InvalidOperationException(
                $"Cannot send Invoice {Id}: its gross amount is {GrossAmount.Amount} and must be greater than zero.");
        }

        Status = InvoiceStatus.Sent;
    }

    /// <summary>
    /// StateMachine.md §3.3: <c>Sent → Overdue</c> when "DueDate &lt; today and not yet Paid".
    /// Only from <see cref="InvoiceStatus.Sent"/> — §3.2's diagram draws that one edge into
    /// <c>Overdue</c> and no other, and the "not yet Paid" half of the guard is exactly what
    /// restricting the source state already expresses.
    ///
    /// <para>
    /// <b>Nothing in Phase 8 calls this on a schedule, and that gap is deliberate rather than
    /// forgotten.</b> The transition itself is real business capability and lives here where it
    /// belongs; what does not yet exist is a job-hosting strategy to run it. Inventing one — a
    /// background service, or an endpoint no document names — to make a roadmap line look complete
    /// was explicitly rejected. See <c>NEXT_STEPS.md</c>.
    /// </para>
    /// <para>
    /// <paramref name="asOf"/> is supplied by the caller rather than read from
    /// <see cref="DateTime.UtcNow"/> here, matching <c>TokenLink.IsExpired</c>: it keeps the rule
    /// deterministic under test, exercised by moving the reading rather than by sleeping or by
    /// reflecting into <see cref="DueDate"/> to backdate it.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The Invoice is not <c>Sent</c>, or is not yet past due.</exception>
    public void MarkOverdue(DateTime asOf)
    {
        EnsureStatus(nameof(MarkOverdue), InvoiceStatus.Sent);

        // Same date-not-instant comparison as Create's due-date guard: an invoice due today is not
        // overdue today. §3.3 says "DueDate < today", which is a comparison between calendar days.
        if (DueDate.Date >= asOf.Date)
        {
            throw new InvalidOperationException(
                $"Cannot mark Invoice {Id} overdue: it is due {DueDate:yyyy-MM-dd}, which is not before {asOf:yyyy-MM-dd}.");
        }

        Status = InvoiceStatus.Overdue;
    }

    /// <summary>
    /// StateMachine.md §3.3: <c>Sent → Paid</c> and <c>Overdue → Paid</c>, neither carrying a guard
    /// beyond the source state. SRS FR-8.4: the Admin confirms payment manually, recording the date
    /// and method — there is no real payment processing in v1.
    ///
    /// <para>
    /// <b>The Payment's amount is this Invoice's own gross amount, always.</b> Phase 8 supports
    /// full payment only: neither FR-8.4, nor Sequence Diagram §9's <c>{ paidAt, method }</c> body,
    /// nor Wireframe E3 offers an amount to supply, so accepting one would invent a partial-payment
    /// capability whose consequences (its effect on <see cref="Status"/>, per-invoice outstanding
    /// balances, overpayment) no document defines. See <see cref="Payment"/>.
    /// </para>
    /// <para>
    /// Returns the created <see cref="Payment"/>, matching <c>Inspection.AddPhoto</c> and
    /// <c>AngebotSection.AddItem</c> — a caller building a response needs the child just created,
    /// not merely the knowledge that the collection grew.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The Invoice is not <c>Sent</c> or <c>Overdue</c>.</exception>
    public Payment MarkPaid(PaymentMethod method, DateTime paidAt, int recordedByAdminId)
    {
        EnsureStatus(nameof(MarkPaid), InvoiceStatus.Sent, InvoiceStatus.Overdue);

        var payment = new Payment(GrossAmount, method, paidAt, recordedByAdminId);
        _payments.Add(payment);
        Status = InvoiceStatus.Paid;

        return payment;
    }

    /// <summary>
    /// StateMachine.md §3.3: <c>Draft → Void</c>, <c>Sent → Void</c> and <c>Overdue → Void</c>.
    /// BR-9: the number is retained, never reused — voiding is a status, and no code path anywhere
    /// in this project deletes an Invoice row.
    ///
    /// <para>
    /// <b>A reason is required for every void, including from <c>Draft</c>.</b> PermissionMatrix.md
    /// §5 states it without qualification ("Admin-only, requires a reason"), while §3.3's
    /// <c>Draft → Void</c> row leaves its guard cell blank where the <c>Sent</c>/<c>Overdue</c> row
    /// says "Admin provides a reason". Treated as an omission in the state-machine table rather
    /// than as an exemption, and reconciled there — the permission matrix and BusinessRules.md are
    /// this project's authorities on rules, and a void with no recorded reason is exactly the
    /// audit ambiguity BR-9's "mark, don't delete" exists to prevent.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    /// <exception cref="InvalidOperationException">The Invoice is already <c>Paid</c> or <c>Void</c>.</exception>
    public void Void(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A void reason is required.", nameof(reason));

        EnsureStatus(nameof(Void), InvoiceStatus.Draft, InvoiceStatus.Sent, InvoiceStatus.Overdue);

        VoidReason = reason.Trim();
        Status = InvoiceStatus.Void;
    }

    private void EnsureStatus(string transitionName, params InvoiceStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException(
                $"Cannot perform '{transitionName}': Invoice {Id} is in status '{Status}', expected {string.Join(" or ", allowed.Select(s => $"'{s}'"))}.");
        }
    }
}
