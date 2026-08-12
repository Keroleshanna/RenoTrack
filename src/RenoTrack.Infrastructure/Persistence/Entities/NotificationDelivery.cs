namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// A durable record of one attempt to deliver one notification (D69).
///
/// <para><b>Infrastructure-only, deliberately not a Domain entity</b> — the same test D49 applied to
/// <see cref="AuditLog"/>: it protects no business invariant, no `BusinessRules.md` rule references
/// it, and no aggregate branches on it. It exists because a committed business operation stays
/// successful when its email fails, and two of the six senders are anonymous public endpoints where
/// no Admin is present to be told — so the failure has to be written down somewhere an Admin can
/// later see it (Slice 4).</para>
///
/// <para><b>What is never stored here</b> (D69, S3-2, S3-4): SMTP credentials, the rendered subject
/// or body, the token, the composed link, raw <c>exception.Message</c>, <c>exception.ToString()</c>,
/// stack traces, or raw SMTP server reply text. The full technical detail stays in the Slice 2
/// <c>Warning</c> log, which already attaches the exception.</para>
///
/// <para>No guards in the constructor: every value it sets is a plain assignment, and EF Core
/// materialises a persisted row through this same constructor, so a time-dependent check here would
/// run on every read (<c>CLAUDE.md</c> §2).</para>
/// </summary>
public sealed class NotificationDelivery
{
    /// <summary>
    /// How <see cref="Recipient"/> joins a multi-address recipient set. Declared here so the value
    /// that is <em>persisted</em>, the value the sender <em>builds</em>, and the value configuration
    /// validation <em>measures</em> can never drift apart — the three used to be three separate
    /// literals, which is precisely how a length check becomes wrong without anyone noticing.
    /// </summary>
    public const string RecipientSeparator = ", ";

    /// <summary>
    /// The column's capacity. **Deliberately not 320.** That is the RFC maximum for *one* address and
    /// is what <c>Leads.Email</c> and <c>Customers.Email</c> use; carrying it over here was a mistake,
    /// because this column holds the complete resolved recipient <em>set</em> — three of the six
    /// notifications go to the configured Admin list. <c>Email:AdminRecipients</c> is validated at
    /// startup against this exact limit, so an over-long list fails there rather than stranding a
    /// successfully-delivered notification as <c>Pending</c>.
    /// </summary>
    public const int MaxRecipientLength = 1000;

    public int Id { get; private set; }

    public NotificationType NotificationType { get; private set; }

    /// <summary>Half of the polymorphic business reference — <c>Lead</c>, <c>Angebot</c> or <c>Invoice</c>.</summary>
    public string EntityType { get; private set; }

    /// <summary>
    /// The other half. **No foreign key**: one column cannot reference three tables, the same
    /// structural impossibility <c>ERD.md</c> already records for <c>TokenLinks</c> and the same
    /// shape <see cref="AuditLog"/> uses (Architecture.md §11, "no cross-entity linkage").
    /// </summary>
    public int EntityId { get; private set; }

    public NotificationDeliveryStatus Status { get; private set; }

    /// <summary>
    /// The complete resolved recipient set the message was actually sent to, recorded as soon as it
    /// is known (S3-3, S3-5). Multiple addresses are joined with <see cref="RecipientSeparator"/> —
    /// an Admin notification goes to every address in <c>Email:AdminRecipients</c>, and "it reached
    /// the office mailbox but not the owner" is exactly what Slice 4 needs to be able to show.
    ///
    /// <para><b>Nullable, and the null has one specific meaning:</b> delivery failed <em>before</em> a
    /// recipient could be resolved — reachable only for the Inspector notification, whose address
    /// comes from <c>InspectorEmailLookup</c> during delivery. Every other notification knows its
    /// recipient before any work begins. <b>Never a sentinel</b> such as "(unresolved)": a non-address
    /// in an address column is worse than an honest null.</para>
    ///
    /// <para>This is a historical fact, not a cached copy of aggregate data: the source aggregate
    /// holds the address <em>as it is now</em>, whereas this holds where the message actually went.</para>
    /// </summary>
    public string? Recipient { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// When the most recent attempt happened. Set at construction, because the row is created for the
    /// attempt that immediately follows, and updated when that attempt concludes.
    /// </summary>
    public DateTime? LastAttemptAt { get; private set; }

    /// <summary>
    /// How many attempts have been made — always <c>1</c> in Slice 3, which performs exactly one and
    /// never retries. A real historical fact rather than scaffolding (S3-1); Slice 5 increments it.
    /// </summary>
    public int AttemptCount { get; private set; }

    public DateTime? SentAt { get; private set; }

    /// <summary>The exception's type name only — never its message (S3-2, S3-4).</summary>
    public string? FailureType { get; private set; }

    /// <summary>
    /// An application-authored, sanitized description chosen from the three approved delivery-phase
    /// categories. Never third-party text: an SMTP server's reply routinely echoes the recipient
    /// address, and persisting it would put PII we did not choose to place there into the database.
    /// </summary>
    public string? FailureMessage { get; private set; }

    public NotificationDelivery(NotificationType notificationType, string entityType, int entityId)
    {
        NotificationType = notificationType;
        EntityType = entityType;
        EntityId = entityId;
        Status = NotificationDeliveryStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        LastAttemptAt = CreatedAt;
        AttemptCount = 1;
    }

    /// <summary>
    /// Records the destination once it is known — after the message is built, so it reflects the
    /// addresses actually on the message rather than the intent behind it.
    /// </summary>
    public void RecordRecipient(string recipient) => Recipient = recipient;

    /// <summary>Terminal transition: the SMTP server accepted the message.</summary>
    public void MarkSent(DateTime utcNow)
    {
        Status = NotificationDeliveryStatus.Sent;
        SentAt = utcNow;
        LastAttemptAt = utcNow;
    }

    /// <summary>
    /// Terminal transition: the attempt ended without delivery. <paramref name="failureType"/> is the
    /// exception's type name; <paramref name="failureMessage"/> is one of the approved sanitized
    /// category descriptions — never text produced by MailKit or by a mail server.
    /// </summary>
    public void MarkFailed(DateTime utcNow, string failureType, string failureMessage)
    {
        Status = NotificationDeliveryStatus.Failed;
        FailureType = failureType;
        FailureMessage = failureMessage;
        LastAttemptAt = utcNow;
    }
}
