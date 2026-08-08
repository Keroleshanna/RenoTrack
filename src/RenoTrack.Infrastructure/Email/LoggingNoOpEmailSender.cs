using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Placeholder only — the real SMTP-backed implementation is Phase 9's deliverable
/// (<c>IEmailSender</c>'s own doc comment; <c>CLAUDE.md</c> §11 — SRS OQ-3, the provider choice,
/// must be resolved first). Unlike <c>PlaceholderFileStorage</c> (Slice 12, which throws),
/// <c>IEmailSender</c>'s own interface doc comment explicitly sanctions a no-op/logging
/// implementation here, so Phase 2's already-built handlers can run end-to-end through Phases
/// 3–8 without a real mail provider — email is fire-and-forget notification (Architecture.md
/// §10), not evidentiary data the way an uploaded photo is (BR-10), so silently not sending one
/// carries no data-integrity risk.
///
/// "No-op" does not mean silent: every call is logged at Warning level with the notification's
/// key details, so it is always visible — in logs, in tests — that no real email was ever sent,
/// rather than the call appearing to succeed with no trace at all.
/// </summary>
public sealed class LoggingNoOpEmailSender(ILogger<LoggingNoOpEmailSender> logger) : IEmailSender
{
    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(nameof(SendNewWebsiteLeadNotificationAsync), $"LeadId={notification.LeadId}, LeadName={notification.LeadName}");
        return Task.CompletedTask;
    }

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(nameof(SendAngebotSubmittedForReviewNotificationAsync), $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}");
        return Task.CompletedTask;
    }

    public Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(nameof(SendAngebotChangesRequestedNotificationAsync), $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The token is deliberately absent from the log line. It is the credential that grants access
    /// to the Angebot, so writing it to every log sink would defeat the point of hashing refresh
    /// tokens (D60) and of generating it from a CSPRNG at all. The AngebotId is enough to correlate.
    /// </summary>
    public Task SendAngebotReadyNotificationAsync(AngebotReadyNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(nameof(SendAngebotReadyNotificationAsync), $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}");
        return Task.CompletedTask;
    }

    public Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(nameof(SendInvoiceReadyNotificationAsync), $"InvoiceId={notification.InvoiceId}, InvoiceNumber={notification.InvoiceNumber}");
        return Task.CompletedTask;
    }

    public Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken)
    {
        LogNotSent(
            nameof(SendAngebotDecisionNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}, Approved={notification.Approved}");
        return Task.CompletedTask;
    }

    private void LogNotSent(string method, string details) =>
        logger.LogWarning(
            "{Method} called, but no real IEmailSender implementation exists yet (Phase 9 deliverable). No email was sent. {Details}",
            method,
            details);
}
