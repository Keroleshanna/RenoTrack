namespace RenoTrack.Application.Angebote.Queries.GetAngebotReviewComments;

/// <summary>
/// One Angebot's review comment history (SRS FR-5.4, PermissionMatrix.md §4).
/// </summary>
/// <param name="RequestingInspectorId">
/// The Inspector whose ownership of the parent Angebot must hold, or <see langword="null"/> for an
/// Admin, who is "F".
/// </param>
public sealed record GetAngebotReviewCommentsQuery(int AngebotId, int? RequestingInspectorId);
