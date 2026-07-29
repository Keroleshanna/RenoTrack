namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a command/query targets an aggregate that doesn't exist. The API layer
/// (Phase 4) maps this to 404 in its ProblemDetails middleware (Architecture.md §5.3).
/// </summary>
public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} with id '{key}' was not found.");
