namespace RenoTrack.Application.Common.Notifications;

/// <summary>
/// SRS FR-9.2's third Admin trigger / Sequence Diagram §6: "Lead X approved/rejected Angebot
/// ANG-2026-00042". Carries no token — the link has served its purpose by the time this is sent,
/// and a credential has no place in an internal notification.
/// </summary>
/// <param name="Approved">
/// True for an approval, false for a rejection. A bool rather than the decision enum because the
/// notification model belongs to <c>Common</c>, which must not depend on a feature folder (the
/// dependency-direction correction recorded in CLAUDE.md §11).
/// </param>
public sealed record AngebotDecisionNotification(
    int AngebotId,
    string AngebotNumber,
    int LeadId,
    string LeadName,
    bool Approved);
