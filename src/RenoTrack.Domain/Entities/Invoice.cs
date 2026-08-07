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
/// <b>Amounts arrive pre-split; this aggregate does not compute them.</b> SRS FR-8.2 requires a
/// VAT breakdown "consistent with the originating Angebot's rates", which is knowledge of a
/// different aggregate and therefore out of reach here (CLAUDE.md §2). The Application layer
/// performs the allocation (Slice 3) and passes the three resulting totals. What this aggregate
/// *does* enforce is that they are coherent: <see cref="NetAmount"/> + <see cref="VatAmount"/>
/// must equal <see cref="GrossAmount"/> exactly, which needs no external knowledge and is the
/// invariant any allocation arithmetic has to satisfy.
/// </para>
/// <para>
/// <b>There is no <c>SentAt</c>, and no per-rate VAT detail.</b> ERD.md's <c>Invoices</c> table
/// defines exactly the columns modelled below; adding a send timestamp (which <c>Angebot</c> has
/// and this does not) or storing the per-rate lines would be inventing schema. The per-rate split
/// is computed to derive the header totals and is deliberately not persisted — <c>InvoiceLine</c>
/// is documented in ERD.md as an *optional* finer breakdown and is deferred out of Phase 8, on
/// ERD.md's own statement that "an Invoice can exist with just header-level Net/VAT/Gross amounts
/// if lines aren't needed".
/// </para>
/// </summary>
public sealed class Invoice
{
    private readonly List<Payment> _payments = [];

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

    public IReadOnlyList<Payment> Payments => _payments;

    /// <summary>
    /// Assignment only — every guard lives in <see cref="Create"/> (CLAUDE.md §2), so nothing
    /// re-runs when EF Core materialises a persisted row through this same private constructor.
    /// Every guard there is a lifetime invariant in any case (ids, a non-blank number, non-negative
    /// amounts that add up), none of them clock-dependent — which is the trap <c>TokenLink</c> fell
    /// into and the reason this split is kept even where it currently costs nothing.
    /// </summary>
    private Invoice(
        int projectId,
        string invoiceNumber,
        DateTime issueDate,
        DateTime dueDate,
        Money netAmount,
        Money vatAmount,
        Money grossAmount)
    {
        ProjectId = projectId;
        InvoiceNumber = invoiceNumber;
        IssueDate = issueDate;
        DueDate = dueDate;
        NetAmount = netAmount;
        VatAmount = vatAmount;
        GrossAmount = grossAmount;
        Status = InvoiceStatus.Draft;
        VoidReason = null;
    }

    /// <summary>
    /// StateMachine.md §3.2: an Invoice is born <c>Draft</c> — the diagram has no other entry
    /// point. Sequence Diagram §8 is the flow.
    ///
    /// <para>
    /// <b><see cref="IssueDate"/> is server-derived and deliberately not a parameter.</b> Sequence
    /// Diagram §8's request body is <c>{ grossAmount, dueDate }</c> and Wireframe E2 collects
    /// exactly those two fields, so the issue date is the moment the invoice comes into existence,
    /// not a caller's choice.
    /// </para>
    /// <para>
    /// The guards are self-guards only. That <paramref name="projectId"/> names a real Project in
    /// an <c>Active</c>/<c>OnHold</c> state is checked by <c>CreateInvoiceCommand</c>
    /// (StateMachine.md §5 assigns it there by name) and backed by a foreign key; that the amounts
    /// reflect the originating Angebot's rate mix is the Application layer's job. What is checked
    /// here needs nothing beyond the arguments themselves.
    /// </para>
    /// <para>
    /// <b><paramref name="dueDate"/> is not constrained</b> — not against the issue date, not
    /// against anything. No requirement document places a rule on it, and adding one here would be
    /// inventing policy rather than implementing it.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The project id is not positive, the invoice number is blank, an amount is negative, or the
    /// amounts do not add up.
    /// </exception>
    public static Invoice Create(
        int projectId,
        string invoiceNumber,
        DateTime dueDate,
        Money netAmount,
        Money vatAmount,
        Money grossAmount)
    {
        if (projectId <= 0)
            throw new ArgumentException("Project id must be positive.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));

        ArgumentNullException.ThrowIfNull(netAmount);
        ArgumentNullException.ThrowIfNull(vatAmount);
        ArgumentNullException.ThrowIfNull(grossAmount);

        if (netAmount.Amount < 0)
            throw new ArgumentException("Net amount cannot be negative.", nameof(netAmount));
        if (vatAmount.Amount < 0)
            throw new ArgumentException("VAT amount cannot be negative.", nameof(vatAmount));
        if (grossAmount.Amount < 0)
            throw new ArgumentException("Gross amount cannot be negative.", nameof(grossAmount));

        // The invariant every VAT allocation must satisfy, stated once, here — so an allocation
        // that loses or invents a cent cannot reach the database. BR-11 rounds each per-rate part,
        // and rounded parts do not automatically re-sum to the figure they were split from; making
        // that the aggregate's guard is what forces the Application layer's residual handling to
        // be deliberate rather than accidental.
        if (netAmount + vatAmount != grossAmount)
        {
            throw new ArgumentException(
                $"Net ({netAmount.Amount}) plus VAT ({vatAmount.Amount}) must equal gross ({grossAmount.Amount}).",
                nameof(grossAmount));
        }

        // No relationship between the due date and the issue date is enforced. It would be an
        // undocumented business rule: no requirement document constrains a due date, so choosing
        // one here would be this phase inventing policy rather than implementing it.
        return new Invoice(
            projectId, invoiceNumber.Trim(), DateTime.UtcNow, dueDate, netAmount, vatAmount, grossAmount);
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
