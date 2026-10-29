namespace RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;

/// <summary>
/// Submits an editable Angebot for internal review. Owning Inspector or any Admin (D115) — an Admin
/// may submit any Angebot in <c>Draft</c>/<c>ChangesRequested</c>, typically after correcting it.
/// </summary>
/// <param name="RequestingInspectorId">The scope: the Inspector's own id, or <c>null</c> for an Admin (D88).</param>
/// <param name="PerformedByUserId">Who submitted — the audit actor (D88).</param>
public sealed record SubmitAngebotForReviewCommand(
    int AngebotId,
    int ExpectedVersion,
    int? RequestingInspectorId,
    int PerformedByUserId);
