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

    /// <summary>
    /// The customer approved via their token link (Phase 6 Slice 4). Logged against <c>Lead</c>,
    /// like <see cref="AngebotSent"/> and <see cref="AngebotCreated"/>, because the transition the
    /// business cares about is the Lead reaching <c>Won</c> (StateMachine.md §5).
    ///
    /// <c>NEXT_STEPS.md</c> anticipated these as <c>LeadWon</c>/<c>LeadLost</c>. They are named for
    /// the Angebot event instead, following the <see cref="AngebotSent"/> precedent set one slice
    /// earlier: the value names *what happened*, the <c>entityType</c> argument names what it
    /// happened to, and "the customer approved the Angebot" is the event — the Lead's move to
    /// <c>Won</c> is its consequence.
    /// </summary>
    AngebotCustomerApproved,

    /// <summary>The customer rejected via their token link — the mirror of <see cref="AngebotCustomerApproved"/>.</summary>
    AngebotCustomerRejected,
    CatalogItemCreated,
    CatalogItemUpdated,
    CatalogItemRetired,

    /// <summary>
    /// An approved Angebot was converted into a Project (FR-7.1, BR-2). Logged against the
    /// <c>Project</c>, per Sequence Diagram §7 — the milestone is the Project coming into
    /// existence, and unlike Angebot creation this drives no Lead-level status change at all
    /// (the Lead already reached <c>Won</c> in the customer's decision handler).
    /// </summary>
    ProjectCreated,

    /// <summary>
    /// An Invoice was created against a Project (FR-8.1, Phase 8 Slice 3). Logged against the
    /// <c>Invoice</c>, not the Project: unlike Angebot creation — which drives
    /// <c>Lead.MarkAngebotInProgress()</c> and is therefore a Lead-level milestone — this drives no
    /// status change on any other aggregate at all. The milestone is the Invoice coming into
    /// existence, and SRS FR-12.1 names "Invoice creation/status changes" as its own audited event.
    /// </summary>
    InvoiceCreated,

    /// <summary>
    /// The Invoice was sent to the customer via a token link (FR-8.3, Phase 8 Slice 4). Logged
    /// against the <c>Invoice</c>, unlike <see cref="AngebotSent"/> which is logged against the Lead
    /// — sending an Angebot drives <c>Lead.MarkAngebotSent()</c>, a pipeline milestone, whereas
    /// sending an Invoice changes no other aggregate's state at all.
    /// </summary>
    InvoiceSent,

    /// <summary>
    /// The Admin manually confirmed payment (FR-8.4, Phase 8 Slice 5). Logged against the
    /// <c>Invoice</c>; the method and date go in <c>details</c>, while the authoritative record is
    /// the <c>Payment</c> child row itself.
    /// </summary>
    InvoicePaid,

    /// <summary>
    /// The Invoice was cancelled (PermissionMatrix.md §5, Phase 8 Slice 5). Logged against the
    /// <c>Invoice</c> **with the reason in <c>details</c>**, which StateMachine.md §3.3 requires
    /// explicitly ("AuditLog entry with reason") on top of storing it on the invoice row itself.
    /// </summary>
    InvoiceVoided,
}
