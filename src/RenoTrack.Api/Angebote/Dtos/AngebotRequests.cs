using RenoTrack.Domain.Enums;

namespace RenoTrack.Api.Angebote.Dtos;

/// <summary>
/// Creates a Draft Angebot for a Lead. <c>LeadId</c> comes from the route and the creating
/// Inspector from the token, never the body (D61).
/// </summary>
/// <param name="InspectionId">
/// Optional link to the Inspection this quote came out of. A genuine input rather than a
/// server-derived value: it names <em>what the work is based on</em>, not who is acting.
/// </param>
public sealed record CreateAngebotRequest(int? InspectionId);

/// <summary>Adds a section to an editable Angebot.</summary>
public sealed record AddSectionRequest(string Title, int SortOrder);

/// <summary>
/// Adds a line item, in either of SRS FR-4.9's two modes: pick a Catalog entry
/// (<paramref name="CatalogItemId"/> set, description/specification/unit pre-filled from it), or
/// type a fully custom line. The existing handler resolves which mode applies; the controller does
/// not branch on it.
/// </summary>
public sealed record AddItemRequest(
    int SectionId,
    int? CatalogItemId,
    string? Description,
    string? Specification,
    string? UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    VatRate VatRate);
