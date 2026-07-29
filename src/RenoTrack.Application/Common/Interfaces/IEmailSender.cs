using RenoTrack.Application.Common.Notifications;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §10. One explicitly-named method per notification (SRS FR-9.2 lists three:
/// new website Lead, Angebot submitted for review, Lead decision received), each taking a
/// dedicated notification model from <c>Application.Common.Notifications</c> — never a feature
/// DTO. This keeps Common the lowest-level part of Application (feature folders depend on
/// Common, never the reverse) instead of Common quietly depending on every feature it happens
/// to send an email about. Implemented for real in Phase 9; until then, Infrastructure can
/// register a no-op/logging implementation so handlers work end-to-end without a real mail
/// provider.
/// </summary>
public interface IEmailSender
{
    /// <summary>SRS FR-9.2: notify Admin when a new Lead is created via the website. Sequence Diagram §1.</summary>
    Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken);

    /// <summary>SRS FR-9.2: notify Admin when an Inspector submits an Angebot for review. Sequence Diagram §5.</summary>
    Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken);
}
