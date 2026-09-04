using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RenoTrack.Application.Common.Exceptions;
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
    /// <summary>
    /// Commits the tracked changes, translating an optimistic-concurrency loss into the
    /// Application layer's own <see cref="ConflictException"/> (D96).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The translation has to happen here, and could not happen in a handler.</b>
    /// <c>DbUpdateConcurrencyException</c> is an EF Core type, and <c>RenoTrack.Application</c>
    /// references FluentValidation and two Microsoft.Extensions abstractions packages and nothing
    /// else (CLAUDE.md §22) — a handler cannot name the type it would need to catch. Infrastructure
    /// referencing Application is the permitted direction, so the layer that owns the mechanism is
    /// also the only layer that can name both sides of the translation. This is D60's boundary
    /// ("business rules live in Application, mechanisms live in Infrastructure") applied to a
    /// failure mode rather than to a service.
    /// </para>
    /// <para>
    /// <b>Deliberately blanket rather than token-specific.</b> A lost optimistic-concurrency race
    /// always means the same thing — the row this unit of work read was changed underneath it — and
    /// 409 is always the honest answer, so there is nothing for a per-entity branch to decide. It
    /// also means a concurrency token added to any future entity is mapped correctly the day it is
    /// added, rather than surfacing as an unmapped 500 until someone notices.
    /// </para>
    /// <para>
    /// <b>The message is deliberately generic and names nothing.</b> The first caller to reach this
    /// path is the anonymous public decision endpoint, where the loser is a real customer holding a
    /// token link; a mapped exception's message becomes the ProblemDetails <c>detail</c> (D59), so
    /// it must not disclose a row id, a table, a timestamp, or which record was contended.
    /// </para>
    /// <para>
    /// <b>Only <c>DbUpdateConcurrencyException</c> is caught, never its <c>DbUpdateException</c>
    /// base.</b> A unique-index violation or a foreign-key violation is a defect, not a conflict,
    /// and must keep surfacing as an unmapped 500 with its stack trace intact rather than being
    /// dressed up as a routine 409 the caller is invited to retry.
    /// </para>
    /// </remarks>
    /// <exception cref="ConflictException">
    /// Another transaction changed a row this unit of work had read. Nothing in this batch was
    /// applied — EF Core wraps the batch in a transaction and rolls all of it back.
    /// </exception>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException(
                "This request could not be completed because the same record was changed at the same " +
                "time by someone else. Nothing was saved. Please reload and try again.",
                exception);
        }
    }

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
