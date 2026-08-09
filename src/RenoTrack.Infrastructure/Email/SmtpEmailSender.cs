using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Real SMTP delivery over MailKit (D68, SRS OQ-3a), replacing <see cref="LoggingNoOpEmailSender"/>
/// wherever <c>Email:Enabled</c> is true. A company mailbox and a transactional provider's SMTP
/// relay are the same implementation with different configuration, which is what keeps the vendor
/// question (OQ-3b) out of the code entirely.
///
/// <para><b>Failures propagate, deliberately.</b> Slice 1 catches nothing: catch / log /
/// never-rethrow is Slice 2's deliverable, and implementing it here would leave Slice 2's
/// adversarial test — remove the catch, watch a committed operation turn into a 500 — with nothing
/// to remove. Until Slice 2 lands, delivery should not be enabled in any environment.</para>
///
/// <para><b>Nothing is persisted.</b> No <c>NotificationDeliveries</c> row, no status, no retry
/// (Slices 3 and 5).</para>
/// </summary>
public sealed class SmtpEmailSender(
    EmailOptions options,
    EmailMessageFactory messageFactory,
    InspectorEmailLookup inspectorEmailLookup,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken) =>
        SendAsync(
            messageFactory.CreateNewWebsiteLead(notification),
            nameof(SendNewWebsiteLeadNotificationAsync),
            $"LeadId={notification.LeadId}",
            cancellationToken);

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken) =>
        SendAsync(
            messageFactory.CreateAngebotSubmittedForReview(notification),
            nameof(SendAngebotSubmittedForReviewNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    /// <summary>
    /// The only notification whose recipient is a specific person. A missing address is a delivery
    /// failure, never a silent skip (D2) — the Inspector would otherwise never learn that changes
    /// were requested, and nothing else in the system would record that the notification was owed.
    /// </summary>
    public async Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken)
    {
        var inspectorEmail = await inspectorEmailLookup.FindEmailAsync(notification.InspectorId, cancellationToken);

        if (string.IsNullOrWhiteSpace(inspectorEmail))
        {
            throw new InvalidOperationException(
                $"No email address is available for Inspector {notification.InspectorId}, so the " +
                $"'changes requested' notification for Angebot {notification.AngebotNumber} cannot be delivered.");
        }

        await SendAsync(
            messageFactory.CreateAngebotChangesRequested(notification, inspectorEmail),
            nameof(SendAngebotChangesRequestedNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);
    }

    public Task SendAngebotReadyNotificationAsync(AngebotReadyNotification notification, CancellationToken cancellationToken) =>
        SendAsync(
            messageFactory.CreateAngebotReady(notification),
            nameof(SendAngebotReadyNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    public Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken) =>
        SendAsync(
            messageFactory.CreateInvoiceReady(notification),
            nameof(SendInvoiceReadyNotificationAsync),
            $"InvoiceId={notification.InvoiceId}, InvoiceNumber={notification.InvoiceNumber}",
            cancellationToken);

    public Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken) =>
        SendAsync(
            messageFactory.CreateAngebotDecision(notification),
            nameof(SendAngebotDecisionNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}, Approved={notification.Approved}",
            cancellationToken);

    /// <summary>
    /// One connection per message. MailKit's <see cref="SmtpClient"/> is not thread-safe and this
    /// service is registered Scoped (per request), so there is no client to pool — and at this
    /// system's volume there is nothing to gain by inventing one.
    ///
    /// <para><b>What is logged, and what must never be.</b> The notification type and a business
    /// identifier only. Never the recipient address (Lead/Customer personal data, Architecture §12),
    /// never the token, never the composed link — <c>CLAUDE.md</c> §22, the same rule
    /// <see cref="LoggingNoOpEmailSender"/> already follows.</para>
    /// </summary>
    private async Task SendAsync(MimeMessage message, string method, string details, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(options.Host, options.Port, ToSocketOptions(options.SecurityMode), cancellationToken);

            // Only when both halves are configured (D6). An unauthenticated relay is a legitimate
            // deployment, and calling AuthenticateAsync with nothing to send would fail it.
            if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
            {
                await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);

            logger.LogInformation("{Method} delivered. {Details}", method, details);
        }
        finally
        {
            // In a finally so a failed send still closes the connection, but guarded so a disconnect
            // fault can never replace the original exception — that would hide the real cause behind
            // a secondary symptom.
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Disconnecting from the SMTP server failed after {Method}.", method);
                }
            }
        }
    }

    private static SecureSocketOptions ToSocketOptions(EmailSecurityMode mode) => mode switch
    {
        EmailSecurityMode.StartTls => SecureSocketOptions.StartTls,
        EmailSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        EmailSecurityMode.None => SecureSocketOptions.None,

        // Unreachable while EmailSecurityMode has three members; present so adding a fourth without
        // handling it fails loudly here rather than silently selecting a weaker transport.
        _ => throw new InvalidOperationException($"Unsupported {nameof(EmailSecurityMode)} '{mode}'."),
    };
}
