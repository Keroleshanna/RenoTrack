namespace RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;

/// <summary>
/// What the customer chose. Deliberately a different type from
/// <c>PublicAngebotDecision</c>, which the read endpoint returns: this one is imperative and has
/// exactly two values because a request must state an action, while that one is a *state* and
/// needs a third value (<c>Pending</c>) that would be meaningless as an input. Collapsing them
/// would let a caller "decide" Pending.
/// </summary>
public enum CustomerDecision
{
    Approve = 1,
    Reject = 2,
}

/// <summary>
/// SRS FR-6.3 / Sequence Diagram §6 / StateMachine.md §2.3 (<c>Sent → CustomerApproved</c> or
/// <c>Sent → CustomerRejected</c>). The customer's answer, carried entirely by the token and the
/// chosen outcome — there is no caller identity to derive anything from (Architecture.md §7.2).
///
/// <para>
/// <b>The optional rejection reason (FR-6.3) arrived in Phase 11 Slice 5, D98</b>, resolving the
/// ADR Phase 6 deferred. It is stored on the <c>Angebot</c> aggregate itself, never in
/// <c>AuditLog</c> (best-effort by D50) and never as an <c>AngebotReviewComment</c> (whose
/// <c>AdminUserId</c> is a required FK to <c>AspNetUsers</c>, so a customer's words cannot be
/// written there honestly).
/// </para>
/// </summary>
/// <param name="Reason">
/// FR-6.3's optional reason, meaningful <b>only</b> with <see cref="CustomerDecision.Reject"/>. An
/// approval carrying one is refused with a 400 rather than silently dropped — K-4/D67's existing
/// rule, applied unchanged. Trimming and the final length guard belong to the aggregate.
/// </param>
public sealed record RecordAngebotDecisionCommand(
    string Token,
    CustomerDecision Decision,
    string? Reason = null);
