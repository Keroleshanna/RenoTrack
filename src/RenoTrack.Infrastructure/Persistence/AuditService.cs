using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// Best-Effort Audit strategy (D50): every handler already calls IUnitOfWork.SaveChangesAsync()
/// (the business commit) before calling this — no handler calls SaveChangesAsync again
/// afterward, so this method must independently commit its own write. Business consistency never
/// depends on that write succeeding: any failure is caught and logged as a warning, never
/// rethrown, so an already-committed business operation is never reported as failed because of a
/// secondary audit-write fault.
///
/// <para><b>Caveat on what "independently" means here.</b> LogAsync calls SaveChangesAsync on the
/// <em>same scoped RenoTrackDbContext</em> the rest of the request uses — independently of
/// IUnitOfWork, not in an isolated context. Its intended usage is strictly after the primary
/// business commit, at which point nothing is pending. If it is ever called while unrelated tracked
/// changes are still pending, its SaveChangesAsync will flush those changes too.</para>
///
/// <para>Consequences a caller must not get wrong: this is <b>not</b> transaction isolation, and
/// callers must not rely on this service to isolate pending tracked changes. Keep the established
/// ordering — commit the business operation first, then audit. (Found empirically during Phase 4
/// Slice 9, where an audit write masked a deliberately-introduced defect by committing a mutation
/// that should never have reached the database.)</para>
/// </summary>
public sealed class AuditService(RenoTrackDbContext dbContext, ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(
        string entityType,
        int entityId,
        AuditAction action,
        int? performedByUserId,
        string? details,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = new AuditLog(entityType, entityId, action, performedByUserId, details);
            dbContext.AuditLogs.Add(auditLog);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist audit log entry for {EntityType} {EntityId}, action {Action}. The business operation this audit entry describes has already been committed and is unaffected.",
                entityType,
                entityId,
                action);
        }
    }
}
