using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;

/// <summary>
/// No validator: the query carries no fields to validate. No IOwnershipValidator: both roles
/// have full ("F") access per PermissionMatrix.md §6. Deliberately thin — all BR-12 filtering
/// and projection logic lives in whatever implements ICatalogItemQueries, not here.
/// </summary>
public sealed class SearchCatalogItemsQueryHandler(
    ICatalogItemQueries catalogItemQueries) : IQueryHandler<SearchCatalogItemsQuery, IReadOnlyList<CatalogItemDto>>
{
    public Task<IReadOnlyList<CatalogItemDto>> HandleAsync(SearchCatalogItemsQuery query, CancellationToken cancellationToken) =>
        catalogItemQueries.SearchAsync(cancellationToken);
}
