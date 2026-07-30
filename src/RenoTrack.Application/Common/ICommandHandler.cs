namespace RenoTrack.Application.Common;

/// <summary>
/// One handler per use case (Architecture.md §5.1's CQRS-lite). Deliberately not built on a
/// mediator library (e.g. MediatR) — every execution path stays a plain, explicit method call
/// a reader can jump straight to, which matters for a project meant to teach how it's built,
/// not just to run.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
