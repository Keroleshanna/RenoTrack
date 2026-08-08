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

    /// <summary>Sequence Diagram §5: notify the owning Inspector when Admin requests changes.</summary>
    Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// SRS FR-9.1 / Sequence Diagram §6: email the Lead their token link when an Angebot is sent.
    /// The first method here whose recipient is the customer rather than internal staff.
    /// </summary>
    Task SendAngebotReadyNotificationAsync(AngebotReadyNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// SRS FR-9.1 / Sequence Diagram §9: email the customer their token link when an Invoice is
    /// sent. FR-9.1 names Angebot and Invoice together; this is the Invoice half.
    /// </summary>
    Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// SRS FR-9.2's third Admin trigger / Sequence Diagram §6: the customer has approved or
    /// rejected. Completes the three notifications FR-9.2 enumerates.
    /// </summary>
    Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken);
}
