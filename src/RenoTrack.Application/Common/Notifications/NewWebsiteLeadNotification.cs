namespace RenoTrack.Application.Common.Notifications;

/// <summary>
/// The data <see cref="Interfaces.IEmailSender.SendNewWebsiteLeadNotificationAsync"/> actually
/// needs to render the "new Lead from Website" email (SRS FR-9.2, Sequence Diagram §1) — a
/// service contract in its own right, not a reuse of <c>LeadDto</c>. Deliberately a narrower
/// projection than the full Lead: a notification email has no use for Address/Notes/Status/
/// AssignedInspectorId/CreatedAt, so this type doesn't carry them.
/// </summary>
public sealed record NewWebsiteLeadNotification(int LeadId, string LeadName, string LeadPhone, string LeadEmail);
