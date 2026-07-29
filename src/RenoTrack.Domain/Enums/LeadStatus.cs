namespace RenoTrack.Domain.Enums;

/// <summary>
/// The closed set of pipeline positions a Lead can occupy.
/// This is the authoritative enum for StateMachine.md §1 — the transition table there
/// (§1.3) is the only source of truth for which moves between these values are legal.
/// BR-7 requires every change to happen through an explicit, named action (never a
/// silent side effect), which is only enforceable if this set is closed.
/// </summary>
public enum LeadStatus
{
    /// <summary>Just created, not yet acted on. StateMachine.md §1.1.</summary>
    New,

    /// <summary>An on-site Inspection has been booked. StateMachine.md §1.1, FR-2.3.</summary>
    InspectionScheduled,

    /// <summary>Inspector has completed the visit; Angebot can now be drafted. StateMachine.md §1.1, FR-3.4.</summary>
    InspectionDone,

    /// <summary>
    /// An Angebot exists and is being drafted/reviewed internally. Mirrors the Angebot's
    /// internal states (Draft/InReview/ChangesRequested/ApprovedInternally) without
    /// duplicating that state machine on the Lead itself. StateMachine.md §1.1.
    /// </summary>
    AngebotInProgress,

    /// <summary>The Angebot has been sent to the Lead and awaits their decision. StateMachine.md §1.1, FR-6.1.</summary>
    AngebotSent,

    /// <summary>Terminal — the Lead approved the Angebot and it became a Project. StateMachine.md §1.1, BR-2.</summary>
    Won,

    /// <summary>Terminal — the Lead rejected the Angebot. StateMachine.md §1.1, §1.4.</summary>
    Lost
}
