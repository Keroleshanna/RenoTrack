using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed record RecordedAuditCall(string EntityType, int EntityId, AuditAction Action, int? PerformedByUserId, string? Details);

public sealed class FakeAuditService : IAuditService
{
    public List<RecordedAuditCall> Calls { get; } = [];

    public Task LogAsync(string entityType, int entityId, AuditAction action, int? performedByUserId, string? details, CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedAuditCall(entityType, entityId, action, performedByUserId, details));
        return Task.CompletedTask;
    }
}
