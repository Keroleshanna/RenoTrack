namespace RenoTrack.Api.CatalogItems.Dtos;

/// <summary>
/// Creates a Catalog entry directly (PermissionMatrix.md §6, Admin curation). The acting Admin comes
/// from the token, never the body (D61); <c>CreatedFromAngebotItemId</c> is deliberately absent,
/// since provenance is a fact about how an entry was born, not a caller-supplied value.
/// </summary>
public sealed record CreateCatalogItemRequest(
    string Title,
    string DefaultUnitCode,
    decimal SuggestedUnitPrice,
    string? DefaultSpecification);

/// <summary>
/// Edits an existing Catalog entry (PermissionMatrix.md §6, Admin only). BR-8 means past Angebote
/// already hold their own copies and are unaffected.
/// </summary>
public sealed record UpdateCatalogItemRequest(
    string Title,
    string DefaultUnitCode,
    decimal SuggestedUnitPrice,
    string? DefaultSpecification);
