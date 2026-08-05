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
    InspectionScheduled,
    InspectionDone,
    AngebotCreated,
    AngebotSubmittedForReview,
    AngebotApproved,
    AngebotChangesRequested,

    /// <summary>
    /// The Angebot was sent to the customer via a token link (Phase 6 Slice 2). Logged against
    /// <c>Lead</c>, not <c>Angebot</c>, because this is the command that drives
    /// <c>Lead.MarkAngebotSent()</c> — a Lead-level pipeline milestone — exactly as
    /// <c>AngebotCreated</c> is logged against Lead for driving <c>MarkAngebotInProgress()</c>
    /// (CLAUDE.md §10). Contrast the purely internal review actions above, which never touch
    /// Lead.Status and are therefore logged against Angebot.
    /// </summary>
    AngebotSent,
    CatalogItemCreated,
    CatalogItemUpdated,
    CatalogItemRetired,
}
