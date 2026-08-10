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
/// <para><b>A delivery failure never fails the business operation (Phase 9 Slice 2, D69).</b> Every
/// notification is raised <em>after</em> its handler's <c>SaveChangesAsync</c> has already committed,
/// so by the time this class runs there is nothing left to roll back and nothing useful to tell the
/// caller — on four of the six notifications the caller is an anonymous customer who could not act on
/// it anyway. A failure is therefore caught here, logged at <c>Warning</c> with the original
/// exception attached, and swallowed. This is D50's Best-Effort Audit shape applied to the second
/// best-effort side effect in the system, not a new idea.</para>
///
/// <para><b>Nothing is persisted and nothing is retried.</b> No <c>NotificationDeliveries</c> row, no
/// status, no attempt counter (Slice 3), no retry (Slice 5). Until Slice 3 lands, a failed
/// notification exists only in the log — a known, accepted gap, not an oversight.</para>
/// </summary>
public sealed class SmtpEmailSender(
    EmailOptions options,
    EmailMessageFactory messageFactory,
    InspectorEmailLookup inspectorEmailLookup,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            _ => Task.FromResult(messageFactory.CreateNewWebsiteLead(notification)),
            nameof(SendNewWebsiteLeadNotificationAsync),
            $"LeadId={notification.LeadId}",
            cancellationToken);

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            _ => Task.FromResult(messageFactory.CreateAngebotSubmittedForReview(notification)),
            nameof(SendAngebotSubmittedForReviewNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    /// <summary>
    /// The only notification whose recipient is a specific person. Resolving that address is part of
    /// delivering the notification, so it sits <b>inside</b> the guarded region: a missing address
    /// (D2) is a delivery failure like any other, reported the same way rather than thrown at a
    /// handler that has already committed its work.
    /// </summary>
    public Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            async token =>
            {
                var inspectorEmail = await inspectorEmailLookup.FindEmailAsync(notification.InspectorId, token);

                if (string.IsNullOrWhiteSpace(inspectorEmail))
                {
                    throw new InvalidOperationException(
                        $"No email address is available for Inspector {notification.InspectorId}, so the " +
                        $"'changes requested' notification for Angebot {notification.AngebotNumber} cannot be delivered.");
                }

                return messageFactory.CreateAngebotChangesRequested(notification, inspectorEmail);
            },
            nameof(SendAngebotChangesRequestedNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    public Task SendAngebotReadyNotificationAsync(AngebotReadyNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            _ => Task.FromResult(messageFactory.CreateAngebotReady(notification)),
            nameof(SendAngebotReadyNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    public Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            _ => Task.FromResult(messageFactory.CreateInvoiceReady(notification)),
            nameof(SendInvoiceReadyNotificationAsync),
            $"InvoiceId={notification.InvoiceId}, InvoiceNumber={notification.InvoiceNumber}",
            cancellationToken);

    public Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            _ => Task.FromResult(messageFactory.CreateAngebotDecision(notification)),
            nameof(SendAngebotDecisionNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}, Approved={notification.Approved}",
            cancellationToken);

    /// <summary>
    /// The notification failure boundary. It covers the <b>complete</b> delivery operation —
    /// recipient resolution, message construction, and transport — because all three are ways the
    /// same notification can fail, and a caller that has already committed can act on none of them.
    ///
    /// <para><b>Message construction is deferred into this method rather than passed in already
    /// built.</b> Building it at the call site would place a <c>MailboxAddress</c> parse of a stored
    /// customer address, and the Inspector lookup, <em>outside</em> the try — so a malformed address
    /// in the database would still reach the handler as an exception. That is precisely the class of
    /// failure this boundary exists to absorb.</para>
    ///
    /// <para><b>Cancellation is swallowed too</b>, deliberately: it still cancels the SMTP operation,
    /// but the business operation has already committed, so surfacing it as a failed request would
    /// misreport what happened. D50's <c>catch (Exception)</c> already has this property.</para>
    ///
    /// <para><b>A failing logger is not guarded against</b> — an exception from
    /// <see cref="ILogger"/> would escape this catch. That exposure is identical to D50's and is
    /// accepted rather than papered over with a nested try, which would make the one place that
    /// reports problems the one place that hides them.</para>
    /// </summary>
    private async Task DeliverAsync(
        Func<CancellationToken, Task<MimeMessage>> buildMessage,
        string method,
        string details,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await buildMessage(cancellationToken);

            await SendAsync(message, cancellationToken);

            logger.LogInformation("{Method} delivered. {Details}", method, details);
        }
        catch (Exception exception)
        {
            // The original exception is attached, not just its message: D59 established that a
            // swallowed fault stays diagnosable only if its stack trace survives.
            logger.LogWarning(
                exception,
                "{Method} could not be delivered. The business operation it notifies about has already been " +
                "committed and is unaffected; the notification is lost and is not retried automatically. {Details}",
                method,
                details);
        }
    }

    /// <summary>
    /// One connection per message. MailKit's <see cref="SmtpClient"/> is not thread-safe and this
    /// service is registered Scoped (per request), so there is no client to pool — and at this
    /// system's volume there is nothing to gain by inventing one.
    ///
    /// <para><b>What is logged, and what must never be.</b> The notification type and a business
    /// identifier only. Never the recipient address (Lead/Customer personal data, Architecture §12),
    /// never the token, never the composed link, never the message body, never the SMTP credentials —
    /// <c>CLAUDE.md</c> §22, the same rule <see cref="LoggingNoOpEmailSender"/> already follows.</para>
    /// </summary>
    private async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
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
        }
        finally
        {
            // In a finally so a failed send still closes the connection, but swallowed so a
            // disconnect fault can never replace the original exception — that would hide the real
            // cause behind a secondary symptom. Not logged here: DeliverAsync reports the failure
            // that matters, and a second entry about the socket would only dilute it.
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Nothing useful can be done about a failed disconnect: the message has either
                    // been accepted or it has not, and the connection is discarded either way.
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
