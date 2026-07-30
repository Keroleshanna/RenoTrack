using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.CatalogItems.Dtos;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// First query implementation (not a repository) — projects directly to CatalogItemDto inline in
/// the Select, rather than via CatalogItemMappingExtensions.ToDto(), so EF Core translates the
/// whole query (WHERE !IsRetired, column selection) into a single SQL SELECT with no full-entity
/// materialization. AsNoTracking(): a pure read with no follow-up mutation, so change tracking
/// adds nothing. No IUnitOfWork dependency — nothing here is ever committed. BR-12/D37: always
/// excludes retired items, no parameters — this is the only place IsRetired is ever filtered
/// (CatalogItemRepository.GetByIdAsync deliberately does not, per BR-14/D38).
/// </summary>
public sealed class CatalogItemQueries(RenoTrackDbContext dbContext) : ICatalogItemQueries
{
    public async Task<IReadOnlyList<CatalogItemDto>> SearchAsync(CancellationToken cancellationToken) =>
        await dbContext.CatalogItems
            .AsNoTracking()
            .Where(c => !c.IsRetired)
            .OrderBy(c => c.Title)
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
}
