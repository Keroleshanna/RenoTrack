namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// An explicit database transaction spanning more than one <see cref="IUnitOfWork.SaveChangesAsync"/>
/// call, owned by the caller that opened it.
///
/// <para>
/// <b>There is deliberately no <c>RollbackAsync</c>.</b> Disposing an uncommitted transaction rolls
/// it back, so <c>await using</c> already covers every escape path — exception, early return,
/// cancellation — and an explicit method would be a redundant second way to do one thing. Verified
/// against real LocalDB rather than assumed: a row inserted inside a transaction that was disposed
/// without committing did not survive.
/// </para>
/// <para>
/// The type is <see cref="IAsyncDisposable"/> (BCL) rather than EF Core's
/// <c>IDbContextTransaction</c>, so no Infrastructure mechanism leaks into the Application layer's
/// contract — the constraint D48 set when it kept <c>IUnitOfWork</c> minimal.
/// </para>
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>
    /// Makes every change saved inside this transaction permanent. Not calling it — for any
    /// reason — leaves them all rolled back at disposal.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
