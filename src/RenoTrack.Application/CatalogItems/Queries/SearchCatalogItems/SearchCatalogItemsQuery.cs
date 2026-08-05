using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;

/// <summary>
/// The Catalog picker and the Catalog list (Wireframes D2 and F1). PermissionMatrix.md §6 marks
/// "View Catalog" as "F" for both roles — the Catalog is shared company-wide, so nothing here is
/// scoped to a caller.
/// </summary>
/// <remarks>
/// <para>
/// Gained a search term and paging in Phase 5, when it acquired its first real caller. Both are
/// <b>documented</b>, not anticipated: Wireframe D2's picker shows a "Search Catalog" box, and
/// Architecture.md §5.1 makes <c>?page=</c>/<c>?pageSize=</c> the convention for list endpoints. The
/// Catalog grows organically from every Inspector's "save as Catalog item" (FR-4.10), so it is
/// genuinely unbounded — unlike a Lead's Angebot list, which StateMachine.md §2.4 bounds.
/// </para>
/// <para>
/// <b>D37 is unchanged.</b> That decision was specifically about an <c>includeRetired</c> flag, and
/// no such parameter exists: retired items are still excluded unconditionally (BR-12), because no
/// document yet shows them anywhere. This record's earlier comment also claimed no search term or
/// pagination was documented, which was wrong on both counts — see the wireframe and §5.1 above.
/// </para>
/// </remarks>
/// <param name="SearchTerm">
/// Matched case-insensitively against title and specification. Absent or blank means "everything",
/// which is what the picker shows before the user types.
/// </param>
public sealed record SearchCatalogItemsQuery(
    string? SearchTerm = null,
    int Page = Pagination.FirstPage,
    int PageSize = Pagination.DefaultPageSize);
