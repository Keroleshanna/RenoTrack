using RenoTrack.Application.Common;

namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// Infrastructure-only persistence model — deliberately not a Domain entity (D49): it protects
/// no business invariant, never transitions, and no BusinessRules.md rule references it. A plain
/// write-once record, not the rich-domain-model shape used for every actual aggregate.
/// </summary>
public sealed class AuditLog
{
    public int Id { get; private set; }
    public string EntityType { get; private set; }
    public int EntityId { get; private set; }
    public AuditAction Action { get; private set; }
    public int? PerformedByUserId { get; private set; }
    public string? Details { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public AuditLog(string entityType, int entityId, AuditAction action, int? performedByUserId, string? details)
    {
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        PerformedByUserId = performedByUserId;
        Details = details;
        CreatedAt = DateTime.UtcNow;
    }
}
