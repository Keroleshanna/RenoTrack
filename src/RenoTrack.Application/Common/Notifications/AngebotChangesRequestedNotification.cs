namespace RenoTrack.Application.Common.Notifications;

/// <summary>
/// Sequence Diagram §5: "Notify Inspector with comment." Not covered by SRS FR-9.2's own
/// enumeration (that list is Admin-facing notifications only), but explicitly depicted in the
/// sequence diagram — the Inspector needs to know changes were requested and why.
/// </summary>
public sealed record AngebotChangesRequestedNotification(int AngebotId, string AngebotNumber, string Comment, int InspectorId);
