using RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;

namespace RenoTrack.Api.Public.Dtos;

/// <summary>
/// The customer's answer (SRS FR-6.3, Wireframe A3's "Angebot annehmen" / "Ablehnen"). The token
/// comes from the route, so the outcome is the only genuine input — and unlike every other request
/// record in this API, there is no caller identity to omit, because a token-link customer has no
/// account at all (Architecture.md §7.2).
/// </summary>
/// <remarks>
/// Sequence Diagram §6 draws the body as <c>{ result, reason? }</c>. <b>The reason is deliberately
/// absent</b>: where it would be stored is an open architecture decision, it must not go into
/// <c>AuditLog</c> (D50 — audit is best-effort, business data must never depend on it), and
/// accepting a value only to discard it would break the reasonable expectation that anything the
/// API accepts is kept. Not accepting one is the honest contract until that ADR is made. This is a
/// tracked gap against FR-6.3, recorded in <c>NEXT_STEPS.md</c>.
/// </remarks>
public sealed record RecordDecisionRequest(CustomerDecision Decision);
