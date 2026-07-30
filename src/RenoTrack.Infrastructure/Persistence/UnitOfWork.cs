using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// A thin wrapper over RenoTrackDbContext.SaveChangesAsync — deliberately nothing more (Phase 3
/// design review). Does not implement IDisposable: the DbContext is injected, not owned, so its
/// disposal belongs to the DI container's scope, not to this class.
/// </summary>
public sealed class UnitOfWork(RenoTrackDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
