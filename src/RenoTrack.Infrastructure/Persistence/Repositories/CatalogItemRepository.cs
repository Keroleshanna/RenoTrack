using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// No child entities, no navigation — same shape as LeadRepository. GetByIdAsync deliberately
/// does not filter by IsRetired (BR-14/D38: a retired item remains a valid direct reference);
/// that filtering belongs to ICatalogItemQueries.SearchAsync, not this repository.
/// </summary>
public sealed class CatalogItemRepository(RenoTrackDbContext dbContext) : ICatalogItemRepository
{
    public async Task AddAsync(CatalogItem catalogItem, CancellationToken cancellationToken) =>
        await dbContext.CatalogItems.AddAsync(catalogItem, cancellationToken);

    public async Task<CatalogItem?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.CatalogItems.FindAsync([id], cancellationToken);
}
