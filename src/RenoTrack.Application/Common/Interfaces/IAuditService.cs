namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §11: "Audit log as an independent table, written by a small IAuditService
/// called from handlers at key transition points." <paramref name="performedByUserId"/> null
/// means system-triggered (ERD.md: "Nullable user = system-triggered action").
/// <paramref name="action"/> is the strongly-typed <see cref="AuditAction"/>, not a free
/// string — Infrastructure (Phase 3) converts it (<c>action.ToString()</c>) when writing to
/// AuditLog's string column.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string entityType,
        int entityId,
        AuditAction action,
        int? performedByUserId,
        string? details,
        CancellationToken cancellationToken);
}
