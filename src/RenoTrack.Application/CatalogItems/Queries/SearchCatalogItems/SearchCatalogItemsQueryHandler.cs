using FluentValidation;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;

/// <summary>
/// No IOwnershipValidator: both roles have full ("F") access per PermissionMatrix.md §6.
/// Deliberately thin — BR-12's retired-item filtering, the search predicate, and the projection all
/// live in whatever implements ICatalogItemQueries, not here.
/// </summary>
public sealed class SearchCatalogItemsQueryHandler(
    IValidator<SearchCatalogItemsQuery> validator,
    ICatalogItemQueries catalogItemQueries) : IQueryHandler<SearchCatalogItemsQuery, PagedResult<CatalogItemDto>>
{
    public async Task<PagedResult<CatalogItemDto>> HandleAsync(
        SearchCatalogItemsQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await catalogItemQueries.SearchAsync(
            query.SearchTerm,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
