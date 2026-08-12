using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;

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
/// <para><b>Every attempt is recorded (Phase 9 Slice 3, D69).</b> A <c>Pending</c>
/// <see cref="NotificationDelivery"/> row is persisted before the attempt and updated to
/// <c>Sent</c> or <c>Failed</c> — so a failure an anonymous caller could never be told about is
/// still visible to an Admin later. The database gets a sanitized summary; the exception itself
/// stays in the log. <b>Nothing is retried</b>: Slice 5 owns retry, and there is no queue, worker or
/// scheduler anywhere in this path.</para>
/// </summary>
public sealed class SmtpEmailSender(
    EmailOptions options,
    EmailMessageFactory messageFactory,
    InspectorEmailLookup inspectorEmailLookup,
    RenoTrackDbContext dbContext,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            NotificationType.NewWebsiteLead,
            nameof(Lead),
            notification.LeadId,
            _ => Task.FromResult(messageFactory.CreateNewWebsiteLead(notification)),
            nameof(SendNewWebsiteLeadNotificationAsync),
            $"LeadId={notification.LeadId}",
            cancellationToken);

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            NotificationType.AngebotSubmittedForReview,
            nameof(Angebot),
            notification.AngebotId,
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
            NotificationType.AngebotChangesRequested,
            nameof(Angebot),
            notification.AngebotId,
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
            NotificationType.AngebotReady,
            nameof(Angebot),
            notification.AngebotId,
            _ => Task.FromResult(messageFactory.CreateAngebotReady(notification)),
            nameof(SendAngebotReadyNotificationAsync),
            $"AngebotId={notification.AngebotId}, AngebotNumber={notification.AngebotNumber}",
            cancellationToken);

    public Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            NotificationType.InvoiceReady,
            nameof(Invoice),
            notification.InvoiceId,
            _ => Task.FromResult(messageFactory.CreateInvoiceReady(notification)),
            nameof(SendInvoiceReadyNotificationAsync),
            $"InvoiceId={notification.InvoiceId}, InvoiceNumber={notification.InvoiceNumber}",
            cancellationToken);

    public Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken) =>
        DeliverAsync(
            NotificationType.AngebotDecision,
            nameof(Angebot),
            notification.AngebotId,
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
        NotificationType notificationType,
        string entityType,
        int entityId,
        Func<CancellationToken, Task<MimeMessage>> buildMessage,
        string method,
        string details,
        CancellationToken cancellationToken,
        NotificationDelivery? existing = null)
    {
        var delivery = existing ?? new NotificationDelivery(notificationType, entityType, entityId);
        var phase = DeliveryPhase.Preparation;
        Exception? failure = null;

        try
        {
            if (existing is null)
            {
                // Persisted Pending before the attempt, after the handler's own commit (D69). A crash
                // before this line loses the record entirely — the accepted window; a crash after it
                // leaves a Pending row, which is the honest statement that an attempt never concluded.
                dbContext.NotificationDeliveries.Add(delivery);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var message = await buildMessage(cancellationToken);

            // The addresses actually on the message, so the record reflects where it really went
            // rather than the intent behind it (S3-3).
            delivery.RecordRecipient(string.Join(
                NotificationDelivery.RecipientSeparator,
                message.To.Mailboxes.Select(mailbox => mailbox.Address)));

            phase = DeliveryPhase.Transport;
            await SendAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            failure = exception;

            // The original exception is attached, not just its message: D59 established that a
            // swallowed fault stays diagnosable only if its stack trace survives. This log remains
            // the only place the technical detail exists — the database gets a sanitized summary.
            logger.LogWarning(
                exception,
                "{Method} could not be delivered. The business operation it notifies about has already been " +
                "committed and is unaffected; the notification is lost and is not retried automatically. {Details}",
                method,
                details);
        }

        await RecordOutcomeAsync(delivery, failure, phase, method, details);
    }

    /// <summary>
    /// The Slice 5 retry entry point: one more attempt against a delivery row that <b>already
    /// exists</b> and has already been claimed (S5-1, S5-2).
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately a thin forward into <see cref="DeliverAsync"/> rather than a second
    /// delivery path.</b> That method's <c>try</c>/<c>catch</c> stays the single failure boundary
    /// Slice 2 established — retry inherits the identical semantics (log the original exception,
    /// swallow, persist a sanitized terminal state) because it is literally the same code, not
    /// because two implementations were kept in step by hand.</para>
    ///
    /// <para><b><c>internal</c>, not public:</b> reachable from <c>NotificationRetryExecutor</c> and
    /// the Infrastructure test project, and from nowhere else. Widening it would put a
    /// bring-your-own-delivery-row method on the public surface, which is exactly the shape D69
    /// keeps out of Application.</para>
    ///
    /// <para>The row is passed in already tracked and already incremented. This method never calls
    /// <c>Add</c> and never inserts — <b>a retry updates the existing row or it does nothing</b>
    /// (S5-1). The three identity arguments are read back off the row rather than re-supplied, so a
    /// caller cannot accidentally retry one notification under another's identity.</para>
    /// </remarks>
    internal Task RetryAsync(
        NotificationDelivery delivery,
        Func<CancellationToken, Task<MimeMessage>> buildMessage,
        string method,
        string details,
        CancellationToken cancellationToken) =>
        DeliverAsync(
            delivery.NotificationType,
            delivery.EntityType,
            delivery.EntityId,
            buildMessage,
            method,
            details,
            cancellationToken,
            existing: delivery);

    /// <summary>
    /// Writes the terminal state. Separate from the boundary above rather than nested inside its
    /// <c>catch</c>, so there is still exactly one place that decides a notification failed — this
    /// only records the decision.
    ///
    /// <para>Uses <see cref="CancellationToken.None"/> deliberately: a cancelled request is itself an
    /// outcome worth recording, and passing the cancelled token would throw here and lose it.</para>
    ///
    /// <para>Its own failure is swallowed and logged for the same reason everything else here is: a
    /// business operation that already committed must not fail because a bookkeeping row could not be
    /// written. The cost is a row stranded in <c>Pending</c>, which Slice 4 will show as such.</para>
    /// </summary>
    private async Task RecordOutcomeAsync(
        NotificationDelivery delivery,
        Exception? failure,
        DeliveryPhase phase,
        string method,
        string details)
    {
        try
        {
            if (failure is null)
            {
                delivery.MarkSent(DateTime.UtcNow);
                logger.LogInformation("{Method} delivered. {Details}", method, details);
            }
            else
            {
                delivery.MarkFailed(DateTime.UtcNow, failure.GetType().Name, SanitizedFailureMessage(failure, phase));
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "{Method} completed but its delivery record could not be updated; it remains Pending. {Details}",
                method,
                details);
        }
    }

    /// <summary>
    /// One of the three approved category descriptions (S3-2), chosen by <b>delivery phase</b> rather
    /// than exception type — the phase is exact, whereas type-matching would be a heuristic
    /// (<see cref="InvalidOperationException"/> is thrown both by the recipient guard and by the
    /// security-mode switch).
    ///
    /// <para><b>Nothing here is derived from the exception's text.</b> MailKit surfaces the SMTP
    /// server's reply in its message, and real servers routinely echo the recipient address — so
    /// persisting it would put third-party text, and PII nobody chose to store there, into the
    /// database (S3-2, S3-4).</para>
    /// </summary>
    private static string SanitizedFailureMessage(Exception failure, DeliveryPhase phase) =>
        failure is OperationCanceledException
            ? "Delivery was cancelled before it completed."
            : phase switch
            {
                DeliveryPhase.Preparation => "The notification could not be prepared.",
                DeliveryPhase.Transport => "The mail server could not be reached or rejected the message.",
                _ => "The notification could not be prepared.",
            };

    /// <summary>How far delivery had progressed when it failed. Local to this class; nothing persists it.</summary>
    private enum DeliveryPhase
    {
        Preparation,
        Transport,
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
