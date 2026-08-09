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
    /// A Project was marked <c>Completed</c> (FR-7.3, FR-8.6, Phase 8 Slice 6). Logged against the
    /// <c>Project</c> — StateMachine.md §4.3's own side-effect column says the Lead is already
    /// <c>Won</c> and needs no further change, so nothing else's state moves.
    ///
    /// <para>
    /// <b>Every successful completion is logged, not only an override.</b> §4.3 names an AuditLog
    /// entry on its override row alone, but SRS FR-12.1 requires "status changes" generally and
    /// CLAUDE.md §10 classes a terminal workflow transition as a business milestone. On the normal
    /// path <c>details</c> is <see langword="null"/>; on the override path it carries the Admin's
    /// reason, which FR-8.6 makes mandatory and §4.3 requires to be recorded here.
    /// </para>
    /// <para>
    /// <b>The AuditLog row is the override reason's only home</b> (Phase 8 Slice 6, decision K-7):
    /// <c>ERD.md</c>'s <c>PROJECT</c> defines no column for it and none was invented. That inherits
    /// D50's best-effort semantics — a failed audit write is swallowed — so the reason is not a
    /// guaranteed business record. Recorded as a known limitation in <c>NEXT_STEPS.md</c> rather
    /// than papered over.
    /// </para>
    /// </summary>
    ProjectCompleted,

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
