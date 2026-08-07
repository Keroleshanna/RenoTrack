using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    /// <summary>
    /// Set to make <see cref="SaveChangesAsync"/> throw, so a handler's behaviour after a failed
    /// commit can be exercised — a real database failure cannot be provoked from a unit test, and
    /// the compensation path is precisely the code that only runs then.
    /// </summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>
    /// Set to make the <see cref="SaveChangesAsync"/> call at this 1-based position throw, leaving
    /// earlier ones succeeding. Needed because the conversion handler's create-Customer path saves
    /// twice, and "the second save failed" is a different scenario from "the first did".
    /// </summary>
    public int? SaveFailureOnCall { get; set; }

    public int BeginTransactionCallCount { get; private set; }
    public int CommitCallCount { get; private set; }
    public int TransactionDisposeCallCount { get; private set; }

    /// <summary>
    /// True when a transaction was opened and disposed without being committed — the shape a real
    /// rollback takes. This fake cannot prove a database rolls anything back (only
    /// <c>RenoTrack.Infrastructure.Tests</c> can, against real LocalDB); it proves the handler
    /// reaches disposal without committing, which is the orchestration half of the guarantee.
    /// </summary>
    public bool TransactionRolledBack => TransactionDisposeCallCount > 0 && CommitCallCount == 0;

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        // Counted before throwing, so a test asserting "the commit was attempted once" still holds
        // when that attempt failed.
        if (SaveFailure is not null && (SaveFailureOnCall is null || SaveFailureOnCall == SaveChangesCallCount))
        {
            throw SaveFailure;
        }

        return Task.CompletedTask;
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        BeginTransactionCallCount++;
        return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(this));
    }

    private sealed class FakeTransaction(FakeUnitOfWork owner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.CommitCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.TransactionDisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
