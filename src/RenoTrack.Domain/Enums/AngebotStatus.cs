namespace RenoTrack.Domain.Enums;

/// <summary>
/// The closed set of states an Angebot can be in. Authoritative source: StateMachine.md §2,
/// whose transition table (§2.3) and invariants (§2.4) are the only place legal moves and
/// locking rules are defined. BR-1 (must be approved before sending) and the "edit locked
/// once InReview" invariant both depend on this set being closed and exhaustive.
/// </summary>
public enum AngebotStatus
{
    /// <summary>Inspector is actively building the Angebot. Editable. StateMachine.md §2.1.</summary>
    Draft,

    /// <summary>Submitted to Admin, awaiting internal decision. Locked from editing. StateMachine.md §2.1, FR-5.1.</summary>
    InReview,

    /// <summary>Admin sent it back with comments. Returns to Draft the moment the Inspector edits again. StateMachine.md §2.1, FR-5.2(b).</summary>
    ChangesRequested,

    /// <summary>Admin approved internally; about to be / already being sent. StateMachine.md §2.1, BR-1.</summary>
    ApprovedInternally,

    /// <summary>Token link emailed to the Lead; awaiting customer decision. StateMachine.md §2.1, FR-6.1.</summary>
    Sent,

    /// <summary>Terminal — Lead approved via token link. StateMachine.md §2.1, FR-6.3.</summary>
    CustomerApproved,

    /// <summary>Terminal — Lead rejected via token link. StateMachine.md §2.1, FR-6.3.</summary>
    CustomerRejected
}
