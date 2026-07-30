namespace RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;

/// <summary>
/// PermissionMatrix.md §6: Admin curation ("create/curate directly"). <see cref="DefaultUnitCode"/>
/// is the wire-format code ItemUnit.FromCode round-trips (e.g. "m2", "Stk") — Domain owns that
/// mapping, this command just carries the string across the boundary.
/// </summary>
public sealed record CreateCatalogItemCommand(
    string Title,
    string DefaultUnitCode,
    decimal SuggestedUnitPrice,
    string? DefaultSpecification,
    int CreatedByAdminUserId);
