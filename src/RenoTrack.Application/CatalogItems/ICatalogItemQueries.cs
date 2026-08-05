using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems;

public interface ICatalogItemQueries
{
    /// <summary>
    /// Non-retired Catalog items, optionally narrowed by a search term, ordered by title.
    /// </summary>
    /// <remarks>
    /// Retired items are excluded unconditionally and there is no flag to include them (BR-12, D37) —
    /// no document shows retired items surfaced anywhere.
    /// </remarks>
    Task<PagedResult<CatalogItemDto>> SearchAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
