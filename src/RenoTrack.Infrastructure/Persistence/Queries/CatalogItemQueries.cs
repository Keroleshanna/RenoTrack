using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// A query implementation (not a repository) — projects directly to CatalogItemDto inline in the
/// Select, rather than via CatalogItemMappingExtensions.ToDto(), so EF Core translates the whole
/// query into a single SQL SELECT with no full-entity materialization. AsNoTracking(): a pure read
/// with no follow-up mutation, so change tracking adds nothing. No IUnitOfWork dependency — nothing
/// here is ever committed. BR-12/D37: always excludes retired items, with no flag to include them —
/// this is the only place IsRetired is ever filtered (CatalogItemRepository.GetByIdAsync
/// deliberately does not, per BR-14/D38).
/// </summary>
public sealed class CatalogItemQueries(RenoTrackDbContext dbContext) : ICatalogItemQueries
{
    public async Task<PagedResult<CatalogItemDto>> SearchAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CatalogItems
            .AsNoTracking()
            .Where(c => !c.IsRetired);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();

            // EF.Functions.Like rather than string.Contains, so matching uses the column's own
            // collation — case-insensitive on this database — instead of depending on how the
            // provider happens to translate a .NET string comparison.
            query = query.Where(c =>
                EF.Functions.Like(c.Title, $"%{term}%")
                || (c.DefaultSpecification != null && EF.Functions.Like(c.DefaultSpecification, $"%{term}%")));
        }

        // Counted before paging, so the total reflects the filter rather than the page.
        var totalCount = await query.CountAsync(cancellationToken);

        // Ordering is not optional — Skip/Take over an unordered query can repeat or omit rows
        // between pages. Title is the picker's own order (Wireframe D2); Id breaks ties, since
        // titles are not unique.
        var items = await query
            .OrderBy(c => c.Title)
            .ThenBy(c => c.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Select(c => new CatalogItemDto(
                c.Id,
                c.Title,
                c.DefaultSpecification,
                c.DefaultUnit.Code,
                c.SuggestedUnitPrice.Amount,
                c.CreatedFromAngebotItemId,
                c.IsRetired,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CatalogItemDto>(items, page, pageSize, totalCount);
    }
}
