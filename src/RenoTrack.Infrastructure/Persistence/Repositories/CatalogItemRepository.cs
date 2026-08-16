using Microsoft.EntityFrameworkCore;
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

    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c>: the caller returns this entity's DTO on the idempotent
    /// path and the unit of work is shared, so leaving it tracked keeps the behaviour identical to
    /// <see cref="GetByIdAsync"/> above.
    /// <para>
    /// <c>SingleOrDefaultAsync</c> rather than <c>FirstOrDefault</c> — one line yields at most one
    /// entry, and if that ever stops being true it is a bug worth failing on rather than silently
    /// picking one of two.
    /// </para>
    /// </remarks>
    public async Task<CatalogItem?> GetByCreatedFromAngebotItemIdAsync(
        int angebotItemId,
        CancellationToken cancellationToken) =>
        await dbContext.CatalogItems
            .SingleOrDefaultAsync(c => c.CreatedFromAngebotItemId == angebotItemId, cancellationToken);
}
