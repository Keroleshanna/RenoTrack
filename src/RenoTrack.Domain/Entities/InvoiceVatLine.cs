using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// One VAT rate's share of an Invoice: the rate, and the net and VAT amounts billed at it
/// (Phase 14 Slice 2, <b>D111</b>). A child of the Invoice aggregate, created only by
/// <see cref="Invoice.Create"/>.
///
/// <para>
/// <b>This is a calculated result, not an invoice line.</b> It carries no description, no quantity
/// and no unit price, so it is not the deferred <c>InvoiceLine</c> of ERD.md. It is the per-rate
/// split <see cref="VatAllocation"/> derives from the originating Angebot's rate mix, kept because
/// BR-5 requires the document to state "applicable VAT rate(s) and VAT amount(s)" and BR-6 allows
/// one Invoice to carry several. Phase 8 computed exactly this and discarded it.
/// </para>
/// <para>
/// <b>No caller can supply one.</b> The constructor is <c>internal</c>, and the only code that calls
/// it is <see cref="Invoice.Create"/>, from its own allocation. The API accepts no rates and no
/// amounts other than the Admin's chosen gross; the Invoice aggregate decides how that gross splits.
/// </para>
/// <para>
/// <b>Immutable, like every figure on an Invoice.</b> No setter is public and no method changes a
/// line, because an issued invoice is corrected by voiding it and issuing another (BR-9), never by
/// editing.
/// </para>
/// </summary>
public sealed class InvoiceVatLine
{
    public VatRate Rate { get; private set; }
    public Money NetAmount { get; private set; }
    public Money VatAmount { get; private set; }

    /// <summary>
    /// Guards here are lifetime invariants (non-null, non-negative), so they are safe to re-run
    /// when EF Core materialises a row through this constructor (CLAUDE.md §2).
    /// </summary>
    internal InvoiceVatLine(VatRate rate, Money netAmount, Money vatAmount)
    {
        ArgumentNullException.ThrowIfNull(netAmount);
        ArgumentNullException.ThrowIfNull(vatAmount);

        if (netAmount.Amount < 0)
            throw new ArgumentException("Net amount cannot be negative.", nameof(netAmount));
        if (vatAmount.Amount < 0)
            throw new ArgumentException("VAT amount cannot be negative.", nameof(vatAmount));

        Rate = rate;
        NetAmount = netAmount;
        VatAmount = vatAmount;
    }
}
