namespace RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;

/// <summary>
/// PermissionMatrix.md §6: "View Catalog" — Admin F, Inspector F, no scoping. No parameters:
/// see ARCHITECTURE_DECISIONS.md D37 — no includeRetired flag, no search term, no pagination,
/// no sorting, since none is documented as required.
/// </summary>
public sealed record SearchCatalogItemsQuery;
