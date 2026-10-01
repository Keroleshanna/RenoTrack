namespace RenoTrack.Application.Common.Documents;

/// <summary>
/// Everything a rendered Invoice PDF says, assembled by <see cref="InvoiceDocumentFactory"/> from
/// the Invoice aggregate and its Customer (Phase 14 Slice 2, <b>D111</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fields are BR-5's list</b> — the customer's name and address, the invoice date, the unique
/// invoice number, what is billed, the net amount, each applicable VAT rate with its amount, the
/// gross total, and the service date or period when one was given. The issuing company's identity is
/// deliberately absent, exactly as on <see cref="AngebotDocument"/>: it is deployment configuration
/// the generator reads itself.
/// </para>
/// <para>
/// <b>There is no quantity, deliberately.</b> An Invoice carries one description and no line items
/// (D111). Whether that satisfies §14 UStG's "Menge und Art" is a legal reviewer's judgement, not
/// this project's (SRS Q20) — nothing here claims the description stands in for a quantity.
/// </para>
/// <para>
/// <b>Money arrives formatted; dates arrive as dates.</b> The same split <see cref="AngebotDocument"/>
/// makes: figures are formatted once in <see cref="DocumentFormatting"/>, while the date pattern is
/// the renderer's, as it already is for the Angebot.
/// </para>
/// </remarks>
/// <param name="InvoiceNumber">The company-visible number, e.g. <c>RE-2026-00017</c>.</param>
/// <param name="IssuedOn">The invoice date.</param>
/// <param name="DueOn">The date payment is due.</param>
/// <param name="ServicePeriodStart">The service date, or the first day of the period. Absent when none was given.</param>
/// <param name="ServicePeriodEnd">The last day of the period. Absent for a single date.</param>
/// <param name="Customer">Who the invoice is addressed to. The address is always present here.</param>
/// <param name="Description">What the invoice bills for, in the Admin's words.</param>
/// <param name="VatBreakdown">One line per VAT rate billed, as the Invoice aggregate calculated it.</param>
/// <param name="NetTotal">Formatted net total.</param>
/// <param name="VatTotal">Formatted VAT total.</param>
/// <param name="GrossTotal">Formatted gross total.</param>
public sealed record InvoiceDocument(
    string InvoiceNumber,
    DateOnly IssuedOn,
    DateOnly DueOn,
    DateOnly? ServicePeriodStart,
    DateOnly? ServicePeriodEnd,
    DocumentRecipient Customer,
    string Description,
    IReadOnlyList<DocumentVatLine> VatBreakdown,
    string NetTotal,
    string VatTotal,
    string GrossTotal);
