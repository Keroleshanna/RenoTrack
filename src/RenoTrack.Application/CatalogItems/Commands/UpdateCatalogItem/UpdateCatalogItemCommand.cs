namespace RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;

/// <summary>
/// PermissionMatrix.md §6: Admin-only editing. Mirrors CreateCatalogItemCommand's field shape
/// (title/unit/price/specification) plus the Id being updated; never touches
/// CreatedFromAngebotItemId, CreatedAt, or IsRetired — CatalogItem.Update itself doesn't either.
/// </summary>
public sealed record UpdateCatalogItemCommand(
    int Id,
    string Title,
    string DefaultUnitCode,
    decimal SuggestedUnitPrice,
    string? DefaultSpecification,
    int UpdatedByAdminUserId);
