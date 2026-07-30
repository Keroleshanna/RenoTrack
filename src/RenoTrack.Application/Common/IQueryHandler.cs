namespace RenoTrack.Application.Common;

/// <summary>
/// The query-side counterpart to <see cref="ICommandHandler{TCommand, TResult}"/>
/// (Architecture.md §5.1's CQRS-lite). Deliberately a distinct interface, not a reuse of
/// ICommandHandler, even though the method signature currently coincides — commands mutate
/// aggregates via repositories; queries return DTOs directly, bypassing full aggregate
/// hydration entirely (see ARCHITECTURE_DECISIONS.md D36).
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
