using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Invoices.Dtos;

/// <summary>
/// The Invoice as an unauthenticated token-link holder may see it (SRS FR-8.3, Wireframe A4).
///
/// <para>
/// <b>Deliberately a separate hierarchy from <see cref="InvoiceDto"/>, not a projection of it</b> —
/// the rule Phase 6 established for <c>PublicAngebotDto</c>, and for the same reason: if the two
/// shared a type, a field added later for the Dashboard would silently appear on an endpoint any
/// anonymous holder of a forwarded email can reach. The duplication is the safety property.
/// </para>
/// <para>
/// <b>What is absent is absent on purpose.</b> The internal <c>Id</c> and <c>ProjectId</c>, the
/// <c>IssueDate</c>, the <c>Status</c>, <c>VoidReason</c> and every Payment detail are all withheld:
/// Wireframe A4 renders none of them, and the default on this surface is to expose nothing without a
/// documented customer-facing use.
/// </para>
/// <para>
/// <b>Two things A4 shows that are not here, both recorded rather than invented:</b>
/// </para>
/// <para>
/// 1. <b>A VAT percentage.</b> A4 labels the line "VAT (19%)", but an Invoice stores only a
/// <c>VatAmount</c> — there is no rate on the row, because <c>InvoiceLine</c> is deferred out of
/// Phase 8 and the per-rate split is computed at creation and then discarded. Publishing a rate here
/// would mean deriving or assuming one, which on a document of this kind is a fabricated legally
/// relevant figure. The amount is exposed; the label is a Phase 14 / <c>InvoiceLine</c> concern.
/// </para>
/// <para>
/// 2. <b>Bank details for the transfer.</b> No document defines where the company's IBAN/BIC live —
/// not ERD.md, not configuration — so none is invented (approved decision G-5). A4's
/// "[ Download PDF ]" is likewise absent: PDF generation is Phase 14's (G-4).
/// </para>
/// <para>
/// <b><see cref="CustomerName"/> is included</b> because A4's header line renders it
/// ("Project: … — [Customer Name]"). The project *title* on that same line has no column anywhere
/// (flagged in Phase 7, a Phase 12 concern), so it is not here either.
/// </para>
/// </summary>
public sealed record PublicInvoiceDto(
    string InvoiceNumber,
    string CustomerName,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    DateTime DueDate);

public static class PublicInvoiceMappingExtensions
{
    /// <summary>
    /// Takes the customer's name as a separate argument rather than reading it off the Invoice:
    /// <see cref="Invoice"/> has no reference to a <c>Customer</c> and must not acquire one
    /// (CLAUDE.md §2), so the handler resolves it and passes it in.
    /// </summary>
    public static PublicInvoiceDto ToPublicDto(this Invoice invoice, string customerName) => new(
        invoice.InvoiceNumber,
        customerName,
        invoice.NetAmount.Amount,
        invoice.VatAmount.Amount,
        invoice.GrossAmount.Amount,
        invoice.DueDate);
}
