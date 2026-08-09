using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Dtos;

/// <summary>
/// What the customer is told about their own invoice, and nothing more.
///
/// <para>
/// A dedicated public type, never <see cref="InvoiceStatus"/> — the same choice
/// <c>PublicAngebotDecision</c> makes, for the same reason: the public contract stays independent of
/// the internal workflow, so a future internal state cannot accidentally become part of the API a
/// customer's browser depends on.
/// </para>
/// <para>
/// <b><c>Draft</c>, <c>Sent</c> and <c>Overdue</c> all collapse to <see cref="Open"/>.</b> The
/// customer already knows their own due date, and exposing a dunning state is a decision no document
/// makes — so the public surface distinguishes only the three outcomes that change what the page
/// should say: still outstanding, settled, or cancelled.
/// </para>
/// </summary>
public enum PublicInvoiceStatus
{
    /// <summary>Still presented as an outstanding invoice.</summary>
    Open,

    /// <summary>Payment has been recorded.</summary>
    Paid,

    /// <summary>Cancelled — the invoice is no longer payable (BR-9: voided, never deleted).</summary>
    Void,
}

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
/// <c>IssueDate</c>, <c>VoidReason</c> and every Payment detail are all withheld: Wireframe A4
/// renders none of them, and the default on this surface is to expose nothing without a documented
/// customer-facing use. <c>VoidReason</c> in particular is staff-authored text about why the company
/// cancelled a bill — the customer is told *that* it was cancelled, never the internal wording.
/// </para>
/// <para>
/// <b><see cref="Status"/> is the one field added beyond A4, deliberately and by explicit
/// decision.</b> A4 draws no status, but once <c>Paid</c> and <c>Void</c> became reachable the
/// absence stopped being neutral: a voided invoice would have gone on rendering as an ordinary
/// payable bill, and a paid one would still have shown a due date as though outstanding. It is a
/// <see cref="PublicInvoiceStatus"/>, never the internal enum — the same shape
/// <c>PublicAngebotDto</c> uses for the customer's decision.
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
    PublicInvoiceStatus Status,
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
        ToPublicStatus(invoice.Status),
        invoice.NetAmount.Amount,
        invoice.VatAmount.Amount,
        invoice.GrossAmount.Amount,
        invoice.DueDate);

    /// <summary>
    /// <c>Draft</c>, <c>Sent</c> and <c>Overdue</c> all mean "still outstanding" to the customer.
    /// Written as an explicit default rather than an exhaustive switch so that a future internal
    /// state cannot leak onto this surface by being forgotten here — the same defensive shape
    /// <c>PublicAngebotMappingExtensions.ToPublicDecision</c> uses.
    /// </summary>
    private static PublicInvoiceStatus ToPublicStatus(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => PublicInvoiceStatus.Paid,
        InvoiceStatus.Void => PublicInvoiceStatus.Void,
        _ => PublicInvoiceStatus.Open,
    };
}
