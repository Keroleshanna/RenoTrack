using RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;

namespace RenoTrack.Api.Public.Dtos;

/// <summary>
/// The customer's answer (SRS FR-6.3, Wireframe A3's "Angebot annehmen" / "Ablehnen"). The token
/// comes from the route, so the outcome is the only genuine input — and unlike every other request
/// record in this API, there is no caller identity to omit, because a token-link customer has no
/// account at all (Architecture.md §7.2).
/// </summary>
/// <remarks>
/// Sequence Diagram §6 draws the body as <c>{ result, reason? }</c>, and as of Phase 11 Slice 5
/// (<b>D98</b>) that is what this accepts. The reason is stored on the <c>Angebot</c> aggregate,
/// never in <c>AuditLog</c> (D50 — audit is best-effort, business data must never depend on it) and
/// never as an <c>AngebotReviewComment</c> (whose <c>AdminUserId</c> is a required FK to
/// <c>AspNetUsers</c>, so a customer's words cannot be written there honestly).
/// </remarks>
/// <param name="Reason">
/// FR-6.3's optional reason, and <b>rejection-only</b>: sending one alongside
/// <see cref="CustomerDecision.Approve"/> is a 400, never a silent drop — the same rule
/// <c>POST /projects/{id}/complete</c> already applies to a reason without an override (K-4/D67).
/// Capped at 1000 characters. <b>It is staff-facing:</b> it is returned on the Dashboard's Angebot
/// detail read and deliberately never echoed through <c>PublicAngebotDto</c>, because the
/// anonymous token is a credential and the public contract is not widened to carry
/// customer-authored free text back through it.
/// </param>
public sealed record RecordDecisionRequest(CustomerDecision Decision, string? Reason = null);
