using Microsoft.Extensions.Configuration;
using MimeKit;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// SMTP delivery settings, bound from the <c>Email</c> configuration section (D68, SRS OQ-3a).
///
/// <para><b>Nothing company-specific has a default.</b> No host, address, display name or recipient
/// is compiled in, because every one of them differs per deployment (OQ-3b) and a plausible-looking
/// default would be mail that silently fails authentication or reaches nobody. When delivery is
/// enabled, an absent or malformed value fails startup naming the exact key, the same shape as the
/// connection-string, JWT, file-storage and token-link checks.</para>
///
/// <para><b>Validation is conditional on <see cref="Enabled"/>, deliberately.</b> A container
/// composed without email configuration is a normal, supported state — it is what every
/// non-production host and both test projects run as — so <see cref="Validate"/> is called only from
/// the delivery-enabled branch of <c>AddInfrastructure</c>. <c>DevelopmentBootstrapOptions.Validate</c>
/// defers behind its own guard for the same reason (D64).</para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The fully-qualified key, used verbatim in every error message.</summary>
    public const string EnabledKey = $"{SectionName}:{nameof(Enabled)}";

    /// <summary>
    /// Whether this deployment sends real email. <see langword="false"/> when the key is absent —
    /// the same fail-safe default as <c>Database:Mode</c> ⇒ <c>Verify</c> and
    /// <c>DevelopmentBootstrap:Enabled</c> ⇒ <c>false</c>. A deployment that configures nothing
    /// sends nothing, and resolves <see cref="LoggingNoOpEmailSender"/> instead.
    ///
    /// <para><b>Delivery is never inferred from the presence of a host.</b> Enabling real mail is an
    /// explicit act, so a half-filled configuration section cannot start mailing customers by
    /// accident.</para>
    /// </summary>
    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public EmailSecurityMode SecurityMode { get; init; } = EmailSecurityMode.StartTls;

    /// <summary>Absent for an unauthenticated relay. Paired with <see cref="Password"/> — see <see cref="Validate"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Never logged, never defaulted. Supplied by user-secrets in Development and environment variables in Production (D64's precedent).</summary>
    public string? Password { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    /// <summary>
    /// Required, not cosmetic: the frozen customer templates interpolate it into the sign-off
    /// (<c>Mit freundlichen Grüßen</c> / <c>{FromDisplayName}</c>), so an empty value would ship a
    /// blank signature to a customer.
    /// </summary>
    public string FromDisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Optional (F8). Absent means no <c>Reply-To</c> header at all — one is never invented.
    /// </summary>
    public string? ReplyToAddress { get; init; }

    /// <summary>
    /// Company-level recipients for the four FR-9.2 notifications (D71). Deliberately independent of
    /// the Identity Admin role: holding that role does not subscribe an account to operational mail,
    /// and appearing here confers no dashboard permission. An empty list fails validation, because
    /// FR-9.2 would otherwise silently never fire.
    /// </summary>
    public IReadOnlyList<string> AdminRecipients { get; init; } = [];

    /// <summary>
    /// Parsed rather than bound wholesale, for <see cref="Enabled"/>'s sake only: this one key
    /// decides whether a host mails real customers, so <c>"yes"</c>, <c>"1 "</c> or <c>"True!"</c>
    /// must produce an unmistakable message instead of binding silently to <see langword="false"/>
    /// and leaving an operator wondering why nothing was sent. The same reasoning as
    /// <c>DevelopmentBootstrapOptions.ReadEnabled</c> and <c>DatabaseInitializationOptions</c>.
    ///
    /// <para><b><see cref="ReadEnabled"/> runs before the binder, and the order is load-bearing.</b>
    /// <c>Get&lt;EmailOptions&gt;()</c> binds this same key and throws its own
    /// <c>InvalidOperationException</c> ("Failed to convert configuration value…") on a value like
    /// <c>"yes"</c> — so binding first would make the hand-written parse unreachable and hand the
    /// operator a framework message instead of one naming the allowed values. Found by an
    /// adversarial experiment: weakening <see cref="ReadEnabled"/> changed nothing, because the
    /// binder was doing the work.</para>
    /// </summary>
    public static EmailOptions FromConfiguration(IConfiguration configuration)
    {
        var enabled = ReadEnabled(configuration);
        var section = configuration.GetSection(SectionName);
        var bound = section.Get<EmailOptions>() ?? new EmailOptions();

        return new EmailOptions
        {
            Enabled = enabled,
            Host = bound.Host,
            Port = bound.Port,
            SecurityMode = bound.SecurityMode,
            Username = bound.Username,
            Password = bound.Password,
            FromAddress = bound.FromAddress,
            FromDisplayName = bound.FromDisplayName,
            ReplyToAddress = bound.ReplyToAddress,
            AdminRecipients = bound.AdminRecipients,
        };
    }

    private static bool ReadEnabled(IConfiguration configuration)
    {
        var configured = configuration[EnabledKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        if (!bool.TryParse(configured, out var enabled))
        {
            throw new InvalidOperationException(
                $"Configuration '{EnabledKey}' has value '{configured}', which is not a valid boolean. " +
                "Allowed values: true, false.");
        }

        return enabled;
    }

    /// <summary>
    /// Called only when <see cref="Enabled"/> is true. Every failure names the exact key at fault.
    /// </summary>
    /// <exception cref="InvalidOperationException">Any required value is missing or malformed.</exception>
    public void Validate()
    {
        RequireNonEmpty(Host, nameof(Host));

        if (Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(Port)}' is required and must be between 1 and 65535.");
        }

        RequireNonEmpty(FromDisplayName, nameof(FromDisplayName));
        RequireAddress(FromAddress, nameof(FromAddress));

        if (ReplyToAddress is not null)
        {
            RequireAddress(ReplyToAddress, nameof(ReplyToAddress));
        }

        ValidateCredentialPair();
        ValidateAdminRecipients();
    }

    /// <summary>
    /// Credentials are all-or-nothing (D6). Both absent is a legitimate unauthenticated relay; both
    /// present authenticates. Exactly one present is always a mistake, and the failure names both
    /// keys rather than only the missing half — an operator who set a username has already decided
    /// they want authentication, and the actionable information is which pair is incomplete.
    /// </summary>
    private void ValidateCredentialPair()
    {
        var hasUsername = !string.IsNullOrWhiteSpace(Username);
        var hasPassword = !string.IsNullOrWhiteSpace(Password);

        if (hasUsername == hasPassword)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Configuration '{SectionName}:{nameof(Username)}' and '{SectionName}:{nameof(Password)}' must be set " +
            $"together: '{SectionName}:{(hasUsername ? nameof(Password) : nameof(Username))}' is missing. " +
            "Leave both unset for an unauthenticated relay.");
    }

    private void ValidateAdminRecipients()
    {
        if (AdminRecipients.Count == 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(AdminRecipients)}' is required and must contain at least one " +
                "address. SRS FR-9.2's Admin notifications would otherwise be raised and silently delivered to nobody.");
        }

        for (var index = 0; index < AdminRecipients.Count; index++)
        {
            RequireAddress(AdminRecipients[index], $"{nameof(AdminRecipients)}:{index}");
        }

        EnsureAdminRecipientsFitTheDeliveryRecord();
    }

    /// <summary>
    /// An Admin notification is delivered to every configured address, and
    /// <see cref="NotificationDelivery.Recipient"/> records that complete set. Validated here against
    /// the <b>exact persisted representation</b> — same join, same separator — because measuring
    /// anything else would let a configuration pass startup and then fail at the database.
    ///
    /// <para><b>Why this is a startup failure rather than a delivery failure.</b> The row that would
    /// fail to insert <em>is</em> the delivery record, so there would be nothing left to write the
    /// failure into: a successfully-sent email would be recorded forever as an unresolved
    /// <c>Pending</c> attempt. Failing at startup, naming the key, matches how every other
    /// configuration error in this codebase behaves. <b>Nothing is truncated</b> — a shortened address
    /// list is a wrong answer to "who was this sent to?", not a smaller one.</para>
    /// </summary>
    private void EnsureAdminRecipientsFitTheDeliveryRecord()
    {
        var persisted = string.Join(NotificationDelivery.RecipientSeparator, AdminRecipients);

        if (persisted.Length <= NotificationDelivery.MaxRecipientLength)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Configuration '{SectionName}:{nameof(AdminRecipients)}' contains {AdminRecipients.Count} addresses, " +
            $"which are recorded together as {persisted.Length} characters — longer than the " +
            $"{NotificationDelivery.MaxRecipientLength} the notification delivery record can hold. " +
            "Configure fewer or shorter addresses; the list is never truncated, because a shortened " +
            "recipient list would misreport who a notification was sent to.");
    }

    private static void RequireNonEmpty(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration '{SectionName}:{key}' is required.");
        }
    }

    /// <summary>
    /// Parsed with MimeKit's own parser rather than a hand-rolled pattern: the address is handed
    /// straight to MimeKit when the message is built, so a value MimeKit rejects could never be sent.
    ///
    /// <para><b>A parse alone is not enough, which a failing test established rather than a reading
    /// of the docs.</b> MimeKit accepts a bare local part with no domain — <c>"office"</c> and
    /// <c>"not-an-address"</c> both parse successfully, because RFC 5321 permits domain-less
    /// addresses for local delivery. That is exactly the shape of a configuration typo, and accepting
    /// it would defer the problem to a delivery failure against a real relay. A domain is therefore
    /// required as well.</para>
    /// </summary>
    private static void RequireAddress(string value, string key)
    {
        RequireNonEmpty(value, key);

        if (!MailboxAddress.TryParse(value, out var mailbox) || !HasDomain(mailbox.Address))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{key}' has value '{value}', which is not a valid email address.");
        }
    }

    private static bool HasDomain(string address)
    {
        var separator = address.LastIndexOf('@');

        return separator > 0 && separator < address.Length - 1;
    }
}
