namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a use case's own business state conflicts with the request — distinct from
/// NotFoundException (resource missing) and ForbiddenException (resource-ownership rule).
/// E.g. StateMachine.md §2.4: a Lead may have only one non-terminal Angebot at a time. The API
/// layer (Phase 4) maps this to 409 in its ProblemDetails middleware (Architecture.md §5.3).
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
