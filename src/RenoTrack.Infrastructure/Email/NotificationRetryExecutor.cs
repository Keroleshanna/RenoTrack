using Microsoft.EntityFrameworkCore;
using MimeKit;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Rebuilds one notification from **currently persisted business data** and hands it to
/// <see cref="SmtpEmailSender"/>'s retry entry point (S5-1).
///
/// <para><b>Two passes, and the split is the whole point (S5-10).</b>
/// <see cref="ValidateAsync"/> is read-only and runs <em>before</em> the claim, so refusing a stale
/// notification mutates nothing at all. <see cref="ExecuteAsync"/> runs after the claim and only
/// ever produces a real delivery outcome. Without that split, a refusal had to invent a terminal
/// state for a row whose attempt never happened — which is exactly the unapproved lifecycle meaning
/// this arrangement exists to avoid.</para>
///
/// <para><b>Reconstruction, not replay — and that is structural rather than careful.</b> This class
/// has no access to a command handler, a repository write method or a unit of work. It can read
/// business state and send an email; it could not re-record a decision or re-issue an Angebot even
/// if someone asked it to (D70). It never generates a token (S5-6).</para>
///
/// <para>Reads <see cref="RenoTrackDbContext"/> directly rather than through Application
/// repositories, matching <see cref="InspectorEmailLookup"/>: no business rule depends on these
/// reads, and routing them through Application would mean adding repository methods
/// (<c>ITokenLinkRepository</c> has no by-entity lookup at all) to serve a concern D69 places
/// squarely in Infrastructure.</para>
/// </summary>
public sealed class NotificationRetryExecutor(
    RenoTrackDbContext dbContext,
    EmailMessageFactory messageFactory,
    InspectorEmailLookup inspectorEmailLookup,
    SmtpEmailSender emailSender)
{
    /// <summary>
    /// Decides whether this notification may be re-sent at all. Returns <see langword="null"/> when it
    /// may, or an application-authored reason when it must not.
    /// </summary>
    /// <remarks>
    /// <para><b>Strictly read-only, and deliberately so (S5-10).</b> It runs before the compare-and-set
    /// claim, so a refusal leaves the delivery row untouched: no status change, no
    /// <c>AttemptCount</c>, no <c>LastAttemptAt</c>, and nothing written to the failure columns. A
    /// refused retry is not an attempt, and the row must not claim otherwise.</para>
    ///
    /// <para><b>What is deliberately *not* checked here:</b> the Inspector's email address. A missing
    /// address is D2's preparation failure — a real attempt that could not be prepared — and it must
    /// stay a recorded <c>Failed</c> outcome, never a 409.</para>
    ///
    /// <para>Everything it reads is re-read inside <see cref="ExecuteAsync"/>, which is not
    /// redundancy: state can change between the two, and anything that has become impossible by then
    /// surfaces through the Slice 2 boundary as an ordinary preparation failure with one of the three
    /// approved category messages (S3-2).</para>
    /// </remarks>
    public async Task<string?> ValidateAsync(
        NotificationType notificationType,
        int entityId,
        CancellationToken cancellationToken)
    {
        switch (notificationType)
        {
            case NotificationType.NewWebsiteLead:
                return await LeadExistsAsync(entityId, cancellationToken)
                    ? null
                    : CannotRebuild($"Lead {entityId}");

            // No staleness rule beyond existence: an Angebot since approved makes this notice stale
            // but harmless, and no business rule forbids re-sending it (S5-6).
            case NotificationType.AngebotSubmittedForReview:
                return await AngebotExistsAsync(entityId, cancellationToken)
                    ? null
                    : CannotRebuild($"Angebot {entityId}");

            case NotificationType.AngebotChangesRequested:
                return await ValidateChangesRequestedAsync(entityId, cancellationToken);

            case NotificationType.AngebotDecision:
                return await ValidateDecisionAsync(entityId, cancellationToken);

            case NotificationType.AngebotReady:
                return await ValidateAngebotReadyAsync(entityId, cancellationToken);

            case NotificationType.InvoiceReady:
                return await ValidateInvoiceReadyAsync(entityId, cancellationToken);

            default:
                // Unreachable while the enum and this switch agree. Reported as a refusal rather than
                // thrown, so a future member added without a retry arm produces a clear 409 instead
                // of an unmapped 500.
                return $"Notifications of type '{notificationType}' cannot be retried.";
        }
    }

    /// <summary>
    /// Performs the attempt against the already-claimed row.
    /// </summary>
    /// <remarks>
    /// <b>Every read happens inside the message builder</b>, which runs inside
    /// <see cref="SmtpEmailSender.DeliverAsync"/>'s guarded region. That is load-bearing: anything
    /// that has become impossible since <see cref="ValidateAsync"/> ran throws there and is recorded
    /// as a preparation failure with an approved category message, rather than escaping this method
    /// as an unmapped 500 or forcing a second refusal path after the claim.
    /// </remarks>
    public Task ExecuteAsync(NotificationDelivery delivery, CancellationToken cancellationToken) =>
        emailSender.RetryAsync(
            delivery,
            token => BuildMessageAsync(delivery, token),
            method: $"Retry of {delivery.NotificationType}",
            details: $"{delivery.EntityType}Id={delivery.EntityId}",
            cancellationToken);

    private async Task<MimeMessage> BuildMessageAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var entityId = delivery.EntityId;

        return delivery.NotificationType switch
        {
            NotificationType.NewWebsiteLead =>
                messageFactory.CreateNewWebsiteLead(await BuildNewWebsiteLeadAsync(entityId, cancellationToken)),

            NotificationType.AngebotSubmittedForReview =>
                messageFactory.CreateAngebotSubmittedForReview(await BuildSubmittedForReviewAsync(entityId, cancellationToken)),

            NotificationType.AngebotChangesRequested =>
                await BuildChangesRequestedMessageAsync(entityId, cancellationToken),

            NotificationType.AngebotDecision =>
                messageFactory.CreateAngebotDecision(await BuildDecisionAsync(entityId, cancellationToken)),

            NotificationType.AngebotReady =>
                messageFactory.CreateAngebotReady(await BuildAngebotReadyAsync(entityId, cancellationToken)),

            NotificationType.InvoiceReady =>
                messageFactory.CreateInvoiceReady(await BuildInvoiceReadyAsync(entityId, cancellationToken)),

            _ => throw new InvalidOperationException(
                $"Notifications of type '{delivery.NotificationType}' cannot be retried."),
        };
    }

    // ---------- validation ----------

    private async Task<string?> ValidateChangesRequestedAsync(int angebotId, CancellationToken cancellationToken)
    {
        if (!await AngebotExistsAsync(angebotId, cancellationToken))
        {
            return CannotRebuild($"Angebot {angebotId}");
        }

        // Not a staleness policy: without a comment there is no message to rebuild at all.
        return await dbContext.AngebotReviewComments.AnyAsync(c => c.AngebotId == angebotId, cancellationToken)
            ? null
            : $"Angebot {angebotId} has no review comment, so this notification cannot be rebuilt.";
    }

    private async Task<string?> ValidateDecisionAsync(int angebotId, CancellationToken cancellationToken)
    {
        var status = await dbContext.Angebote
            .AsNoTracking()
            .Where(a => a.Id == angebotId)
            .Select(a => (AngebotStatus?)a.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return CannotRebuild($"Angebot {angebotId}");
        }

        // Not a staleness rule either: `Approved` is a bool with no column of its own, derivable only
        // from the two terminal decision states. Outside them there is no decision to report, so the
        // message cannot be rebuilt at all. Both states are terminal, so this can only refuse a row
        // whose Angebot never reached a decision.
        return status is AngebotStatus.CustomerApproved or AngebotStatus.CustomerRejected
            ? null
            : $"Angebot {angebotId} carries no customer decision, so this notification cannot be rebuilt.";
    }

    private async Task<string?> ValidateAngebotReadyAsync(int angebotId, CancellationToken cancellationToken)
    {
        if (!await AngebotExistsAsync(angebotId, cancellationToken))
        {
            return CannotRebuild($"Angebot {angebotId}");
        }

        return await ValidateTokenLinkAsync(TokenLinkEntityType.Angebot, angebotId, $"Angebot {angebotId}", cancellationToken);
    }

    private async Task<string?> ValidateInvoiceReadyAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var status = await dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => (InvoiceStatus?)i.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return CannotRebuild($"Invoice {invoiceId}");
        }

        // S5-6: a paid or voided Invoice must never be re-announced as "ready". Both are business
        // states the customer has already been moved past, and BR-9 keeps a voided Invoice's row
        // alive precisely so it stays visible — not so it can be mailed out again.
        if (status is InvoiceStatus.Void or InvoiceStatus.Paid)
        {
            return $"Invoice {invoiceId} is {status} and must not be sent to the customer again.";
        }

        return await ValidateTokenLinkAsync(TokenLinkEntityType.Invoice, invoiceId, $"Invoice {invoiceId}", cancellationToken);
    }

    /// <summary>
    /// Read-only token-link validity. **Never generates a token** (S5-6): minting a fresh link would
    /// make retry a new business action, which D70 forbids outright — and it would hand the customer
    /// a second live credential for the same document.
    /// </summary>
    private async Task<string?> ValidateTokenLinkAsync(
        TokenLinkEntityType entityType, int entityId, string subject, CancellationToken cancellationToken)
    {
        var link = await FindTokenLinkAsync(entityType, entityId, cancellationToken);

        if (link is null)
        {
            return $"{subject} has no token link, so the customer notification cannot be rebuilt.";
        }

        if (link.IsExpired(DateTime.UtcNow))
        {
            return $"The token link for {subject} has expired. Re-sending it would give the customer a dead link.";
        }

        if (link.UsedAt is not null)
        {
            return $"The token link for {subject} has already been used and cannot be sent again (BR-4).";
        }

        return null;
    }

    // ---------- message construction (inside the delivery boundary) ----------

    private async Task<NewWebsiteLeadNotification> BuildNewWebsiteLeadAsync(int leadId, CancellationToken cancellationToken)
    {
        var lead = await FindLeadAsync(leadId, cancellationToken) ?? throw Vanished($"Lead {leadId}");

        // Current values, deliberately: the retry re-sends what is true now, not a snapshot of what
        // was true when the first attempt failed. The row stores no copy to disagree with (D69).
        return new NewWebsiteLeadNotification(lead.Id, lead.Name, lead.Phone, lead.Email);
    }

    private async Task<AngebotSubmittedForReviewNotification> BuildSubmittedForReviewAsync(
        int angebotId, CancellationToken cancellationToken)
    {
        var angebot = await FindAngebotAsync(angebotId, cancellationToken) ?? throw Vanished($"Angebot {angebotId}");

        return new AngebotSubmittedForReviewNotification(angebot.Id, angebot.AngebotNumber, angebot.LeadId);
    }

    /// <summary>
    /// The comment is re-derived as the <b>latest</b> review comment for this Angebot (S5-4).
    ///
    /// <para><b>Accepted historical imprecision, recorded rather than hidden:</b> the delivery row
    /// identifies the Angebot, never the comment, so retrying an older changes-requested notification
    /// after further review cycles sends the newest comment instead of the one the original attempt
    /// carried. Fixing that means a schema change, which S5-4 explicitly declines. Revisit on a real
    /// incident, not on principle.</para>
    /// </summary>
    private async Task<MimeMessage> BuildChangesRequestedMessageAsync(int angebotId, CancellationToken cancellationToken)
    {
        var angebot = await FindAngebotAsync(angebotId, cancellationToken) ?? throw Vanished($"Angebot {angebotId}");

        var comment = await dbContext.AngebotReviewComments
            .AsNoTracking()
            .Where(c => c.AngebotId == angebotId)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw Vanished($"A review comment for Angebot {angebot.AngebotNumber}");

        var notification = new AngebotChangesRequestedNotification(
            angebot.Id, angebot.AngebotNumber, comment.Comment, angebot.CreatedByInspectorId);

        // Resolved here rather than in ValidateAsync, deliberately: a missing Inspector address is
        // D2's preparation failure — a real attempt that could not be prepared — and must stay a
        // recorded Failed outcome, never a 409 refusal.
        var inspectorEmail = await inspectorEmailLookup.FindEmailAsync(notification.InspectorId, cancellationToken);

        if (string.IsNullOrWhiteSpace(inspectorEmail))
        {
            throw new InvalidOperationException(
                $"No email address is available for Inspector {notification.InspectorId}, so the " +
                $"'changes requested' notification for Angebot {notification.AngebotNumber} cannot be delivered.");
        }

        return messageFactory.CreateAngebotChangesRequested(notification, inspectorEmail);
    }

    private async Task<AngebotDecisionNotification> BuildDecisionAsync(int angebotId, CancellationToken cancellationToken)
    {
        var angebot = await FindAngebotAsync(angebotId, cancellationToken) ?? throw Vanished($"Angebot {angebotId}");

        if (angebot.Status is not (AngebotStatus.CustomerApproved or AngebotStatus.CustomerRejected))
        {
            throw Vanished($"A customer decision on Angebot {angebot.AngebotNumber}");
        }

        var lead = await FindLeadAsync(angebot.LeadId, cancellationToken) ?? throw Vanished($"Lead {angebot.LeadId}");

        return new AngebotDecisionNotification(
            angebot.Id,
            angebot.AngebotNumber,
            lead.Id,
            lead.Name,
            Approved: angebot.Status == AngebotStatus.CustomerApproved);
    }

    private async Task<AngebotReadyNotification> BuildAngebotReadyAsync(int angebotId, CancellationToken cancellationToken)
    {
        var angebot = await FindAngebotAsync(angebotId, cancellationToken) ?? throw Vanished($"Angebot {angebotId}");
        var lead = await FindLeadAsync(angebot.LeadId, cancellationToken) ?? throw Vanished($"Lead {angebot.LeadId}");
        var link = await RequireUsableTokenLinkAsync(
            TokenLinkEntityType.Angebot, angebot.Id, $"Angebot {angebot.AngebotNumber}", cancellationToken);

        return new AngebotReadyNotification(angebot.Id, angebot.AngebotNumber, lead.Name, lead.Email, link.Token);
    }

    private async Task<InvoiceReadyNotification> BuildInvoiceReadyAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw Vanished($"Invoice {invoiceId}");

        if (invoice.Status is InvoiceStatus.Void or InvoiceStatus.Paid)
        {
            throw Vanished($"A sendable Invoice {invoice.InvoiceNumber} (it is now {invoice.Status})");
        }

        var project = await dbContext.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == invoice.ProjectId, cancellationToken)
            ?? throw Vanished($"Project {invoice.ProjectId}");

        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == project.CustomerId, cancellationToken)
            ?? throw Vanished($"Customer {project.CustomerId}");

        var link = await RequireUsableTokenLinkAsync(
            TokenLinkEntityType.Invoice, invoice.Id, $"Invoice {invoice.InvoiceNumber}", cancellationToken);

        return new InvoiceReadyNotification(
            invoice.Id,
            invoice.InvoiceNumber,
            customer.Name,
            customer.Email,
            invoice.GrossAmount.Amount,
            invoice.DueDate,
            link.Token);
    }

    private async Task<TokenLink> RequireUsableTokenLinkAsync(
        TokenLinkEntityType entityType, int entityId, string subject, CancellationToken cancellationToken)
    {
        var link = await FindTokenLinkAsync(entityType, entityId, cancellationToken)
            ?? throw Vanished($"A token link for {subject}");

        return link.IsExpired(DateTime.UtcNow) || link.UsedAt is not null
            ? throw Vanished($"A usable token link for {subject}")
            : link;
    }

    // ---------- helpers ----------

    /// <summary>
    /// Ordered <c>CreatedAt DESC, Id DESC</c> and taking the first, never <c>SingleAsync</c> (S5-8).
    /// In practice one link exists per entity — <c>Angebot.Send()</c> guards <c>ApprovedInternally</c>
    /// and <c>Invoice.Send()</c> guards <c>Draft</c> — but no database constraint enforces it, and
    /// <c>SingleAsync</c> would turn a violated assumption into an unmapped 500 instead of a working
    /// retry.
    /// </summary>
    private Task<TokenLink?> FindTokenLinkAsync(
        TokenLinkEntityType entityType, int entityId, CancellationToken cancellationToken) =>
        dbContext.TokenLinks
            .AsNoTracking()
            .Where(t => t.EntityType == entityType && t.EntityId == entityId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Lead?> FindLeadAsync(int leadId, CancellationToken cancellationToken) =>
        dbContext.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);

    private Task<Angebot?> FindAngebotAsync(int angebotId, CancellationToken cancellationToken) =>
        dbContext.Angebote.AsNoTracking().FirstOrDefaultAsync(a => a.Id == angebotId, cancellationToken);

    private Task<bool> LeadExistsAsync(int leadId, CancellationToken cancellationToken) =>
        dbContext.Leads.AsNoTracking().AnyAsync(l => l.Id == leadId, cancellationToken);

    private Task<bool> AngebotExistsAsync(int angebotId, CancellationToken cancellationToken) =>
        dbContext.Angebote.AsNoTracking().AnyAsync(a => a.Id == angebotId, cancellationToken);

    private static string CannotRebuild(string subject) =>
        $"{subject} no longer exists, so this notification cannot be rebuilt.";

    /// <summary>
    /// Thrown from inside the delivery boundary when something <see cref="ValidateAsync"/> had
    /// already approved has since changed. Lands as an ordinary preparation failure — the approved
    /// <c>Preparation</c> category message and the real exception type name (S3-2, S3-4) — rather
    /// than as a second refusal path after the claim.
    /// </summary>
    private static InvalidOperationException Vanished(string subject) =>
        new($"{subject} is no longer available, so the notification could not be rebuilt for retry.");
}
