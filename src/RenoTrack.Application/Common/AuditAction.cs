namespace RenoTrack.Application.Common;

/// <summary>
/// The single source of truth for audit log action names (Architecture.md §11) — no handler
/// may pass a free-typed string, preventing the same event drifting into "Created"/"Create"/
/// "LeadCreated" depending on who wrote the handler. Values are entity-prefixed even though
/// <see cref="Interfaces.IAuditService.LogAsync"/> also takes an explicit <c>entityType</c>:
/// a self-descriptive value (<c>AngebotSubmittedForReview</c>, not just <c>Submitted</c>)
/// reads correctly on its own when scanning a raw list of actions, without cross-referencing
/// the entityType alongside it. Grows by one value per new use case as Phase 2 proceeds,
/// rather than being fully speculated up front.
/// </summary>
public enum AuditAction
{
    LeadCreated,
}
