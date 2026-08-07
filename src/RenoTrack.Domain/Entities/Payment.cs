using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A payment recorded against an Invoice. Child entity of the Invoice aggregate
/// (Architecture.md §6: "Invoice (root) → InvoiceLine (child), Payment (child)") — only ever
/// created through <see cref="Invoice.MarkPaid"/>, never directly, which is why the constructor is
/// <c>internal</c> rather than <c>public</c>, exactly as <see cref="InspectionPhoto"/> is.
///
/// <para>
/// <b>Phase 8 records full payment only, and <see cref="Amount"/> is never caller-supplied.</b>
/// SRS FR-8.4 and Sequence Diagram §9 both describe one action carrying <c>paidAt</c> and
/// <c>method</c> — and no amount — and Wireframe E3 collects exactly those two fields.
/// <see cref="Invoice.MarkPaid"/> therefore always passes the Invoice's own <c>GrossAmount</c>.
/// ERD.md's one-Invoice-to-many-Payments shape is kept because ERD.md defines it, and it is what
/// makes partial payments addable later without a schema redesign — but <b>it must not be read as
/// evidence that partial payments already work</b>. They are deferred until a requirement defines
/// them (what a partial payment does to <c>Invoice.Status</c>, whether an outstanding balance is
/// tracked per invoice, and what happens on overpayment are all unspecified today). A test pins
/// the full-payment behaviour precisely so the schema cannot be mistaken for the semantics.
/// </para>
/// <para>
/// <b><see cref="RecordedByAdminId"/> is an id, never a navigation property</b> — CLAUDE.md §2's
/// rule for every staff reference in this Domain (<c>Lead.AssignedInspectorId</c>,
/// <c>Angebot.ReviewedByAdminId</c> and the rest). The real <c>AspNetUsers</c> foreign key is an
/// Infrastructure concern (Slice 2).
/// </para>
/// </summary>
public sealed class Payment
{
    public int Id { get; private set; }
    public Money Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTime PaidAt { get; private set; }
    public int RecordedByAdminId { get; private set; }

    /// <summary>
    /// Guards here rather than in a factory, matching <see cref="InspectionPhoto"/> — and safe for
    /// the reason CLAUDE.md §2 requires: both conditions are lifetime invariants. A non-null amount
    /// and a positive recorder id are as true when EF Core materialises this row years later as
    /// they were the day it was written. Nothing here is time-dependent, which is the trap
    /// <c>TokenLink</c> fell into.
    /// </summary>
    internal Payment(Money amount, PaymentMethod method, DateTime paidAt, int recordedByAdminId)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (recordedByAdminId <= 0)
            throw new ArgumentException("Recorded-by admin id must be positive.", nameof(recordedByAdminId));

        Amount = amount;
        Method = method;
        PaidAt = paidAt;
        RecordedByAdminId = recordedByAdminId;
    }
}
