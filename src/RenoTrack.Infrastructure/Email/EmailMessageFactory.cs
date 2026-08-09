using System.Globalization;
using MimeKit;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Turns a notification into a finished <see cref="MimeMessage"/>: sender identity, recipients,
/// subject, plaintext German body, and — for the two customer notifications — the public token-link
/// URL (D5, since the base address is deployment configuration Application deliberately cannot see).
///
/// <para><b>Separate from the transport on purpose.</b> Every template can then be verified against
/// the frozen copy with no socket, no server and no network, which is what makes
/// <c>PHASE9_PROGRESS.md</c>'s copy freeze enforceable by a test rather than by review attention.
/// It is not a speculative abstraction: its only consumer, <see cref="SmtpEmailSender"/>, ships in
/// the same slice.</para>
///
/// <para><b>The copy below is FROZEN</b> (S1-2, <c>PHASE9_PROGRESS.md</c> "Slice 1 — approved email
/// copy"). It must not be reworded, shortened, "improved" or re-translated. If an implementation
/// detail ever genuinely requires a change, stop and raise it — do not edit it here.</para>
/// </summary>
public sealed class EmailMessageFactory(EmailOptions options, TokenLinkOptions tokenLinkOptions)
{
    /// <summary>
    /// German formatting for every rendered date and amount (FR-9.3). Explicit rather than ambient:
    /// the culture of a build agent or a production host is not something this project controls, and
    /// a German email showing "8/31/2026" or "$1,234.56" would be a visible defect.
    /// </summary>
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public MimeMessage CreateNewWebsiteLead(NewWebsiteLeadNotification notification) =>
        CreateForAdmins(
            subject: $"Neue Anfrage über die Website: {notification.LeadName}",
            body: $"""
                Über das Kontaktformular der Website ist eine neue Anfrage eingegangen.

                Name:     {notification.LeadName}
                Telefon:  {notification.LeadPhone}
                E-Mail:   {notification.LeadEmail}

                Die Anfrage wurde als neuer Lead im Dashboard angelegt.

                Diese E-Mail wurde automatisch erzeugt.
                """);

    public MimeMessage CreateAngebotSubmittedForReview(AngebotSubmittedForReviewNotification notification) =>
        CreateForAdmins(
            subject: $"Angebot {notification.AngebotNumber} wartet auf Prüfung",
            body: $"""
                Ein Angebot wurde zur internen Prüfung eingereicht.

                Angebot: {notification.AngebotNumber}

                Es kann jetzt im Dashboard geprüft, freigegeben oder zur Überarbeitung
                zurückgegeben werden.

                Diese E-Mail wurde automatisch erzeugt.
                """);

    /// <summary>
    /// Two variants selected by <c>Approved</c>. The rejection variant states no reason and hints at
    /// none: FR-6.3's optional rejection reason is deliberately neither accepted nor stored (Phase 6),
    /// so copy implying one would advertise a field that does not exist.
    /// </summary>
    public MimeMessage CreateAngebotDecision(AngebotDecisionNotification notification) =>
        notification.Approved
            ? CreateForAdmins(
                subject: $"Angebot {notification.AngebotNumber} wurde angenommen",
                body: $"""
                    Der Kunde hat das Angebot angenommen.

                    Angebot: {notification.AngebotNumber}
                    Kunde:   {notification.LeadName}

                    Diese E-Mail wurde automatisch erzeugt.
                    """)
            : CreateForAdmins(
                subject: $"Angebot {notification.AngebotNumber} wurde abgelehnt",
                body: $"""
                    Der Kunde hat das Angebot abgelehnt.

                    Angebot: {notification.AngebotNumber}
                    Kunde:   {notification.LeadName}

                    Diese E-Mail wurde automatisch erzeugt.
                    """);

    /// <summary>
    /// The one notification addressed to a specific person rather than the configured mailbox; the
    /// address is resolved by <see cref="Identity.InspectorEmailLookup"/> and passed in (D1).
    /// </summary>
    public MimeMessage CreateAngebotChangesRequested(
        AngebotChangesRequestedNotification notification,
        string inspectorEmail) =>
        Create(
            recipients: [MailboxAddress.Parse(inspectorEmail)],
            subject: $"Änderungswünsche zu Angebot {notification.AngebotNumber}",
            body: $"""
                Zu Ihrem Angebot {notification.AngebotNumber} wurden Änderungen angefordert.

                Anmerkung:
                {notification.Comment}

                Das Angebot ist im Dashboard wieder bearbeitbar.

                Diese E-Mail wurde automatisch erzeugt.
                """);

    /// <summary>
    /// Customer-facing. No automatic-email sentence (S1-2 decision 3) and no validity period
    /// (decision 4 — the real expiry is <c>TokenLink.ExpiresAt</c>, which this notification does not
    /// carry, so any stated period could drift from the link it describes).
    /// </summary>
    public MimeMessage CreateAngebotReady(AngebotReadyNotification notification) =>
        Create(
            recipients: [new MailboxAddress(notification.RecipientName, notification.RecipientEmail)],
            subject: $"Ihr Angebot {notification.AngebotNumber}",
            body: $"""
                Guten Tag {notification.RecipientName},

                vielen Dank für Ihr Interesse. Ihr Angebot {notification.AngebotNumber} steht für Sie bereit.

                Sie können es hier ansehen und direkt zu- oder absagen:
                {AngebotUrl(notification.Token)}

                Der Link ist persönlich für Sie bestimmt – bitte geben Sie ihn nicht weiter.

                Mit freundlichen Grüßen
                {options.FromDisplayName}
                """);

    /// <summary>
    /// Customer-facing. Carries no bank details, no payment instruction, no VAT rate and no
    /// attachment or claim of one (G-4, G-5): none of them exists in this system, and Wireframe A4's
    /// versions of them are recorded gaps rather than data. "Fällig am" states the notification's own
    /// <c>DueDate</c> as data — it is not an instruction about how to pay.
    /// </summary>
    public MimeMessage CreateInvoiceReady(InvoiceReadyNotification notification) =>
        Create(
            recipients: [new MailboxAddress(notification.RecipientName, notification.RecipientEmail)],
            subject: $"Ihre Rechnung {notification.InvoiceNumber}",
            body: $"""
                Guten Tag {notification.RecipientName},

                Ihre Rechnung {notification.InvoiceNumber} steht für Sie bereit.

                Rechnungsbetrag: {notification.GrossAmount.ToString("C", German)}
                Fällig am:       {notification.DueDate.ToString("d", German)}

                Sie können die Rechnung hier ansehen:
                {InvoiceUrl(notification.Token)}

                Mit freundlichen Grüßen
                {options.FromDisplayName}
                """);

    /// <summary>
    /// The paths are <c>/angebot/</c> and <c>/invoice/</c> exactly as <c>Sequence Diagram.md</c> §6
    /// and §9 write them (D4.1) — note the second is English while the customer-facing copy says
    /// "Rechnung". The target is the public Website origin, never the API. The token is already
    /// URL-safe base64 (<c>TokenLinkService</c>), so no escaping is introduced here.
    /// </summary>
    private string AngebotUrl(string token) => $"{tokenLinkOptions.NormalizedPublicBaseUrl}/angebot/{token}";

    private string InvoiceUrl(string token) => $"{tokenLinkOptions.NormalizedPublicBaseUrl}/invoice/{token}";

    private MimeMessage CreateForAdmins(string subject, string body) =>
        Create(options.AdminRecipients.Select(MailboxAddress.Parse).ToArray(), subject, body);

    private MimeMessage Create(IReadOnlyList<MailboxAddress> recipients, string subject, string body)
    {
        var message = new MimeMessage
        {
            Subject = subject,

            // Plaintext only (S1-2). No HtmlBody, no multipart/alternative: FR-9.3 constrains language
            // and tone, and nothing documents an HTML requirement.
            Body = new TextPart("plain") { Text = body },
        };

        message.From.Add(new MailboxAddress(options.FromDisplayName, options.FromAddress));
        message.To.AddRange(recipients);

        // Only when configured (F8) — a Reply-To is never invented.
        if (options.ReplyToAddress is not null)
        {
            message.ReplyTo.Add(MailboxAddress.Parse(options.ReplyToAddress));
        }

        return message;
    }
}
