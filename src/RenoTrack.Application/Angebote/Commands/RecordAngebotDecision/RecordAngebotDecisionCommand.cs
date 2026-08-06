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
/// <b>No rejection reason, deliberately.</b> SRS FR-6.3 permits an optional reason and Wireframe A3
/// shows the field, but where it would be stored is an open architecture decision: it must not go
/// into <c>AuditLog</c> (best-effort instrumentation by D50 — business data must never depend on
/// it), and accepting a value only to discard it would break the reasonable expectation that
/// anything the API accepts is kept. Until that ADR is made, the honest contract is not to accept
/// one at all. This is a known, documented gap, not an oversight.
/// </para>
/// </summary>
public sealed record RecordAngebotDecisionCommand(string Token, CustomerDecision Decision);
