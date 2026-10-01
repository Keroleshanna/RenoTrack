namespace RenoTrack.Application.Common.Documents;

/// <summary>
/// Everything a rendered Angebot PDF says, assembled by the Application layer from the aggregate
/// (Phase 14, <b>D110</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A dedicated document model, never the Domain entity and never a feature DTO</b> — the same
/// rule the notification models follow (<c>CLAUDE.md</c> §11). It keeps <c>IPdfGenerator</c>'s
/// implementation, which lives in Infrastructure, from depending on either, and it states exactly
/// what appears on paper: a reader of this record knows what the customer will see.
/// </para>
/// <para>
/// <b>Money arrives as formatted text, not as decimals.</b> BR-11's rounding belongs to the Domain,
/// and German formatting ("1.234,56 €") belongs to one tested place; a renderer that formats its own
/// figures is a second opinion about what the company charges. This mirrors the Website's rule that
/// every figure on a customer page is the server's figure (D78), applied to paper.
/// </para>
/// <para>
/// <b>The company's own identity is deliberately absent.</b> It is deployment configuration
/// (<c>CompanyLegalIdentity</c>), read by the generator itself, not business data the Application
/// layer carries around.
/// </para>
/// </remarks>
/// <param name="AngebotNumber">The company-visible number, e.g. <c>ANG-2026-00042</c>.</param>
/// <param name="IssuedOn">The date shown on the document.</param>
/// <param name="Customer">Who the offer is addressed to.</param>
/// <param name="Sections">The offer's sections, in the order they are to be printed.</param>
/// <param name="VatBreakdown">One line per VAT rate present, as the Domain computed it.</param>
/// <param name="NetTotal">Formatted net total.</param>
/// <param name="GrossTotal">Formatted gross total.</param>
public sealed record AngebotDocument(
    string AngebotNumber,
    DateOnly IssuedOn,
    DocumentRecipient Customer,
    IReadOnlyList<DocumentSection> Sections,
    IReadOnlyList<DocumentVatLine> VatBreakdown,
    string NetTotal,
    string GrossTotal);

/// <summary>Who a document is addressed to. Absent lines are omitted rather than printed empty.</summary>
public sealed record DocumentRecipient(string Name, string? Address);

/// <summary>One titled group of lines.</summary>
public sealed record DocumentSection(string Title, IReadOnlyList<DocumentLine> Lines, string Subtotal);

/// <summary>
/// One priced line, with every figure already formatted and every text already the company's own.
/// </summary>
/// <param name="Position">The line's printed position, e.g. <c>1.2</c>.</param>
/// <param name="Description">What the line is for — an Inspector's words, printed verbatim.</param>
/// <param name="Specification">An optional second line of detail.</param>
/// <param name="Quantity">Formatted quantity with its unit, e.g. <c>10,5 m²</c>.</param>
/// <param name="UnitPrice">Formatted unit price.</param>
/// <param name="VatRate">The rate as printed, e.g. <c>19 %</c>.</param>
/// <param name="LineTotal">Formatted line total.</param>
public sealed record DocumentLine(
    string Position,
    string Description,
    string? Specification,
    string Quantity,
    string UnitPrice,
    string VatRate,
    string LineTotal);

/// <summary>One VAT rate's share of a document (BR-6: rates are per line, summarised per rate).</summary>
public sealed record DocumentVatLine(string VatRate, string NetAmount, string VatAmount);
