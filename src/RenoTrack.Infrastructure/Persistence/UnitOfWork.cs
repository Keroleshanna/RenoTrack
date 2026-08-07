using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// A thin wrapper over RenoTrackDbContext — deliberately nothing beyond SaveChangesAsync and, as
/// of Phase 7 Slice 3, opening an explicit transaction (D48 and its amendment). Does not implement
/// IDisposable: the DbContext is injected, not owned, so its disposal belongs to the DI
/// container's scope. A transaction is different — it is created on demand and owned by whoever
/// asked for it, which is why BeginTransactionAsync hands one back rather than holding it here.
///
/// <para>
/// <b>Requires that no retrying execution strategy is configured.</b> `AddInfrastructure` calls
/// `UseSqlServer(connectionString)` with no `EnableRetryOnFailure`, and a retrying strategy
/// forbids user-initiated transactions outright — adding connection resiliency later means
/// revisiting every caller of <see cref="BeginTransactionAsync"/>, not just this class.
/// </para>
/// </summary>
public sealed class UnitOfWork(RenoTrackDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfCoreUnitOfWorkTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    /// <summary>
    /// Keeps EF Core's <see cref="IDbContextTransaction"/> out of the Application layer's
    /// contract. Rollback is disposal: <see cref="DisposeAsync"/> delegates to EF's own, which
    /// rolls back anything uncommitted — so there is nothing to add here beyond passing the call
    /// through, and no separate rollback method to keep in sync.
    /// </summary>
    private sealed class EfCoreUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
