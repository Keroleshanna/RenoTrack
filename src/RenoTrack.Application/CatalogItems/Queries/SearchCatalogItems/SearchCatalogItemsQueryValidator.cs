using FluentValidation;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;

/// <summary>
/// Shape only (CLAUDE.md §5). The search term itself is deliberately unvalidated — any string is a
/// legitimate search, including one that matches nothing.
/// </summary>
public sealed class SearchCatalogItemsQueryValidator : AbstractValidator<SearchCatalogItemsQuery>
{
    public SearchCatalogItemsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Pagination.FirstPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Pagination.MaxPageSize);
    }
}
