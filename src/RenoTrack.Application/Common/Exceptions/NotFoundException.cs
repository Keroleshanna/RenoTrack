namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a command/query targets an aggregate that doesn't exist. The API layer
/// (Phase 4) maps this to 404 in its ProblemDetails middleware (Architecture.md §5.3).
/// </summary>
public sealed class NotFoundException : Exception
{
    /// <summary>
    /// The normal case: an id the caller supplied and already knows, echoed back so the message
    /// says which lookup failed.
    /// </summary>
    public NotFoundException(string entityName, object key)
        : base($"{entityName} with id '{key}' was not found.")
    {
    }

    /// <summary>
    /// For lookups whose key must not appear in the message. Added in Phase 6 Slice 3 for the
    /// public token-link endpoint: the "id" there is the token itself, and since mapped exceptions
    /// surface their message as ProblemDetails <c>detail</c> <i>and</i> are logged at Warning
    /// (D59), the id-based constructor would write a live customer credential into both the
    /// response body and every log sink. The wording is also the customer's rather than a
    /// developer's — Sequence Diagram §6 shows this page telling them the link is not valid.
    /// </summary>
    public NotFoundException(string message)
        : base(message)
    {
    }
}
