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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        // Counted before throwing, so a test asserting "the commit was attempted once" still holds
        // when that attempt failed.
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        return Task.CompletedTask;
    }
}
