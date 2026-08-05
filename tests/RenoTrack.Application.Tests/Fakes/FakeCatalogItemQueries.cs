using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// In-memory fake mirroring the filtering and paging a real implementation must perform — not a
/// dumb passthrough, so handler tests exercise the actual contract: BR-12 excludes retired items,
/// the search term matches title or specification case-insensitively, and the total counts matches
/// rather than the returned page.
/// </summary>
public sealed class FakeCatalogItemQueries : ICatalogItemQueries
{
    private readonly List<CatalogItemDto> _items = [];

    /// <summary>The arguments each call arrived with, so a handler test can assert what it forwarded.</summary>
    public List<(string? SearchTerm, int Page, int PageSize)> Calls { get; } = [];

    public void Seed(CatalogItemDto item) => _items.Add(item);

    public Task<PagedResult<CatalogItemDto>> SearchAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        Calls.Add((searchTerm, page, pageSize));

        var matches = _items.Where(i => !i.IsRetired);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();

            matches = matches.Where(i =>
                i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (i.DefaultSpecification is not null
                    && i.DefaultSpecification.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var all = matches.OrderBy(i => i.Title).ThenBy(i => i.Id).ToList();

        var items = all
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<CatalogItemDto>(items, page, pageSize, all.Count));
    }
}
