namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a resource-ownership business invariant fails (Architecture.md §7.3) — e.g.
/// "only the assigned Inspector may complete this Inspection" (PermissionMatrix.md's "S"
/// rows). Distinct from role-based authorization, which never reaches the Application layer
/// at all (rejected earlier, at the API layer). The API layer (Phase 4) maps this to 403 in
/// its ProblemDetails middleware (Architecture.md §5.3).
/// </summary>
public sealed class ForbiddenException(string message) : Exception(message);
