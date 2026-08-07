namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// The single commit boundary for a use case (Sequence Diagram §4: "SaveChangesAsync (via
/// IUnitOfWork)"). Repository <c>AddAsync</c> calls only stage changes; nothing reaches the
/// database until a handler calls this.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens an explicit transaction so several <see cref="SaveChangesAsync"/> calls commit or
    /// roll back as one. Added in Phase 7 Slice 3 for <c>ConvertAngebotToProjectCommand</c> — the
    /// first command where a brand-new aggregate (<c>Project</c>) needs the database-generated
    /// identity of another brand-new aggregate (<c>Customer</c>) before it can be validly
    /// constructed. See `ARCHITECTURE_DECISIONS.md` D48's amendment for why the alternatives were
    /// rejected.
    ///
    /// <para>
    /// <b>Use it only when a use case genuinely needs more than one save.</b> A single
    /// <see cref="SaveChangesAsync"/> is already atomic through EF Core's own implicit
    /// transaction, and opening one for symmetry adds a lock scope for nothing.
    /// </para>
    /// <para>
    /// <b>The returned transaction is the caller's to dispose</b> — <c>await using</c>, always.
    /// A <c>DbContext</c> must not be reused after a rolled-back transaction: the change tracker
    /// still holds entities as persisted with ids the database no longer has (the D55 family of
    /// hazard). In practice the failure propagates and the request scope is disposed.
    /// </para>
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
