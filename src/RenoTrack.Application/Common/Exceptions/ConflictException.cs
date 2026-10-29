namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a use case's own business state conflicts with the request — distinct from
/// NotFoundException (resource missing) and ForbiddenException (resource-ownership rule).
/// E.g. StateMachine.md §2.4: a Lead may have only one non-terminal Angebot at a time. The API
/// layer (Phase 4) maps this to 409 in its ProblemDetails middleware (Architecture.md §5.3).
/// </summary>
public sealed class ConflictException : Exception
{
    /// <summary>
    /// The one message for "someone else changed this record first" — whether that was detected
    /// by the database (<c>UnitOfWork</c>'s optimistic-concurrency translation, D96) or by a
    /// handler comparing a request's <c>expectedVersion</c> with the loaded aggregate (D115).
    /// </summary>
    /// <remarks>
    /// One constant so the two detections cannot drift into two wordings. It names nothing — no
    /// row, table, id, timestamp or version — because a mapped exception's message becomes the
    /// ProblemDetails <c>detail</c> (D59), and the first caller ever to reach this path was the
    /// anonymous public decision endpoint.
    /// </remarks>
    public const string ConcurrentChangeMessage =
        "This request could not be completed because the same record was changed at the same " +
        "time by someone else. Nothing was saved. Please reload and try again.";

    /// <summary>
    /// The normal case: a business-state conflict this layer detected and phrased itself.
    /// </summary>
    public ConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// For a conflict a lower layer detected as a mechanism failure and translated up. Added in
    /// Phase 11 Slice 1 for <c>UnitOfWork</c>'s optimistic-concurrency translation (D96): the
    /// caller-facing message must name nothing, but the underlying
    /// <c>DbUpdateConcurrencyException</c> has to survive, because D59 logs every mapped exception
    /// at Warning <i>with its full stack trace</i> and that instruction is worthless if the
    /// translation throws the real cause away. Only the message reaches the caller — an inner
    /// exception never appears in a ProblemDetails response.
    /// </summary>
    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
