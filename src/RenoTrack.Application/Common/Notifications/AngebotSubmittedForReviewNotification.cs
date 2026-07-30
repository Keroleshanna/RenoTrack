namespace RenoTrack.Application.Common.Notifications;

/// <summary>SRS FR-9.2 / Sequence Diagram §5: notify Admin an Angebot is ready for review.</summary>
public sealed record AngebotSubmittedForReviewNotification(int AngebotId, string AngebotNumber, int LeadId);
