using MimeKit;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// Enforces the copy freeze (S1-2, <c>PHASE9_PROGRESS.md</c> "Slice 1 — approved email copy").
///
/// <para><b>Every template is asserted against its complete body, never a substring.</b> A
/// contains-check would let a silent reword pass, which is exactly what the freeze exists to
/// prevent — so these assertions are deliberately brittle. If one fails, the question is whether the
/// copy was changed without approval, not whether the test is too strict.</para>
///
/// <para>Line endings are normalized on both sides before comparing: whether a raw string literal
/// yields CRLF or LF depends on how the source file happens to be stored, and that is not what the
/// freeze is about. Every other character is compared exactly.</para>
///
/// <para>No network, no database, no server.</para>
/// </summary>
public sealed class EmailMessageFactoryTests
{
    private const string BaseUrl = "https://www.example.invalid";

    private static EmailMessageFactory CreateFactory(string? replyTo = null) =>
        new(
            new EmailOptions
            {
                Enabled = true,
                Host = "smtp.example.invalid",
                Port = 587,
                FromAddress = "no-reply@example.invalid",
                FromDisplayName = "Beispiel Bau GmbH",
                ReplyToAddress = replyTo,
                AdminRecipients = ["office@example.invalid", "owner@example.invalid"],
            },
            new TokenLinkOptions { LifetimeDays = 30, PublicBaseUrl = BaseUrl });

    private static string Body(MimeMessage message) =>
        Normalize(Assert.IsType<TextPart>(message.Body).Text);

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    [Fact]
    public void NewWebsiteLead_matches_the_frozen_copy()
    {
        var message = CreateFactory().CreateNewWebsiteLead(
            new NewWebsiteLeadNotification(7, "Familie Klein", "0176 1234567", "klein@example.invalid"));

        Assert.Equal("Neue Anfrage über die Website: Familie Klein", message.Subject);
        Assert.Equal(
            Normalize("""
                Über das Kontaktformular der Website ist eine neue Anfrage eingegangen.

                Name:     Familie Klein
                Telefon:  0176 1234567
                E-Mail:   klein@example.invalid

                Die Anfrage wurde als neuer Lead im Dashboard angelegt.

                Diese E-Mail wurde automatisch erzeugt.
                """),
            Body(message));
    }

    [Fact]
    public void AngebotSubmittedForReview_matches_the_frozen_copy()
    {
        var message = CreateFactory().CreateAngebotSubmittedForReview(
            new AngebotSubmittedForReviewNotification(5, "ANG-2026-00005", 7));

        Assert.Equal("Angebot ANG-2026-00005 wartet auf Prüfung", message.Subject);
        Assert.Equal(
            Normalize("""
                Ein Angebot wurde zur internen Prüfung eingereicht.

                Angebot: ANG-2026-00005

                Es kann jetzt im Dashboard geprüft, freigegeben oder zur Überarbeitung
                zurückgegeben werden.

                Diese E-Mail wurde automatisch erzeugt.
                """),
            Body(message));
    }

    [Fact]
    public void AngebotDecision_approved_matches_the_frozen_copy()
    {
        var message = CreateFactory().CreateAngebotDecision(
            new AngebotDecisionNotification(5, "ANG-2026-00005", 7, "Familie Klein", Approved: true));

        Assert.Equal("Angebot ANG-2026-00005 wurde angenommen", message.Subject);
        Assert.Equal(
            Normalize("""
                Der Kunde hat das Angebot angenommen.

                Angebot: ANG-2026-00005
                Kunde:   Familie Klein

                Diese E-Mail wurde automatisch erzeugt.
                """),
            Body(message));
    }

    [Fact]
    public void AngebotDecision_rejected_matches_the_frozen_copy_and_states_no_reason()
    {
        var message = CreateFactory().CreateAngebotDecision(
            new AngebotDecisionNotification(5, "ANG-2026-00005", 7, "Familie Klein", Approved: false));

        Assert.Equal("Angebot ANG-2026-00005 wurde abgelehnt", message.Subject);
        Assert.Equal(
            Normalize("""
                Der Kunde hat das Angebot abgelehnt.

                Angebot: ANG-2026-00005
                Kunde:   Familie Klein

                Diese E-Mail wurde automatisch erzeugt.
                """),
            Body(message));

        // FR-6.3's optional rejection reason is deliberately neither accepted nor stored (Phase 6).
        // Copy must not advertise a field that does not exist.
        Assert.DoesNotContain("Grund", Body(message), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AngebotChangesRequested_matches_the_frozen_copy()
    {
        var message = CreateFactory().CreateAngebotChangesRequested(
            new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Bitte die MwSt. auf 19 % korrigieren.", 3),
            "inspector@example.invalid");

        Assert.Equal("Änderungswünsche zu Angebot ANG-2026-00005", message.Subject);
        Assert.Equal("inspector@example.invalid", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal(
            Normalize("""
                Zu Ihrem Angebot ANG-2026-00005 wurden Änderungen angefordert.

                Anmerkung:
                Bitte die MwSt. auf 19 % korrigieren.

                Das Angebot ist im Dashboard wieder bearbeitbar.

                Diese E-Mail wurde automatisch erzeugt.
                """),
            Body(message));
    }

    [Fact]
    public void AngebotReady_matches_the_frozen_copy()
    {
        var message = CreateFactory().CreateAngebotReady(
            new AngebotReadyNotification(5, "ANG-2026-00005", "Familie Klein", "klein@example.invalid", "tok-abc123"));

        Assert.Equal("Ihr Angebot ANG-2026-00005", message.Subject);
        Assert.Equal(
            Normalize("""
                Guten Tag Familie Klein,

                vielen Dank für Ihr Interesse. Ihr Angebot ANG-2026-00005 steht für Sie bereit.

                Sie können es hier ansehen und direkt zu- oder absagen:
                https://www.example.invalid/angebot/tok-abc123

                Der Link ist persönlich für Sie bestimmt – bitte geben Sie ihn nicht weiter.

                Mit freundlichen Grüßen
                Beispiel Bau GmbH
                """),
            Body(message));
    }

    [Fact]
    public void InvoiceReady_matches_the_frozen_copy_with_de_DE_formatting()
    {
        var message = CreateFactory().CreateInvoiceReady(
            new InvoiceReadyNotification(
                9,
                "RE-2026-00009",
                "Familie Klein",
                "klein@example.invalid",
                1234.56m,
                new DateTime(2026, 8, 31),
                "tok-xyz789"));

        Assert.Equal("Ihre Rechnung RE-2026-00009", message.Subject);
        Assert.Equal(
            Normalize("""
                Guten Tag Familie Klein,

                Ihre Rechnung RE-2026-00009 steht für Sie bereit.

                Rechnungsbetrag: 1.234,56 €
                Fällig am:       31.08.2026

                Sie können die Rechnung hier ansehen:
                https://www.example.invalid/invoice/tok-xyz789

                Mit freundlichen Grüßen
                Beispiel Bau GmbH
                """),
            Body(message));
    }

    /// <summary>
    /// D4.1: the technical path stays English while the customer-facing word is "Rechnung". Pinned
    /// so a well-meaning "consistency" fix cannot quietly retarget every invoice link.
    /// </summary>
    [Fact]
    public void InvoiceUrl_uses_the_invoice_path_not_rechnung()
    {
        var message = CreateFactory().CreateInvoiceReady(
            new InvoiceReadyNotification(9, "RE-2026-00009", "Familie Klein", "klein@example.invalid", 100m, new DateTime(2026, 8, 31), "tok"));

        Assert.Contains("/invoice/tok", Body(message));
        Assert.DoesNotContain("/rechnung/", Body(message));
        Assert.Contains("Rechnung", Body(message));
    }

    [Fact]
    public void A_trailing_slash_on_the_base_url_does_not_produce_a_double_slash()
    {
        var factory = new EmailMessageFactory(
            new EmailOptions { FromAddress = "no-reply@example.invalid", FromDisplayName = "Beispiel", AdminRecipients = ["office@example.invalid"] },
            new TokenLinkOptions { LifetimeDays = 30, PublicBaseUrl = "https://www.example.invalid/" });

        var message = factory.CreateAngebotReady(
            new AngebotReadyNotification(5, "ANG-2026-00005", "Familie Klein", "klein@example.invalid", "tok"));

        Assert.Contains("https://www.example.invalid/angebot/tok", Body(message));
        Assert.DoesNotContain("//angebot", Body(message));
    }

    /// <summary>S1-2 decision 3: internal templates carry the sentence, customer templates must not.</summary>
    [Fact]
    public void The_automatic_email_sentence_appears_only_on_internal_templates()
    {
        var factory = CreateFactory();
        const string Sentence = "Diese E-Mail wurde automatisch erzeugt.";

        Assert.Contains(Sentence, Body(factory.CreateNewWebsiteLead(new NewWebsiteLeadNotification(7, "K", "0176", "k@example.invalid"))));
        Assert.Contains(Sentence, Body(factory.CreateAngebotSubmittedForReview(new AngebotSubmittedForReviewNotification(5, "ANG", 7))));
        Assert.Contains(Sentence, Body(factory.CreateAngebotDecision(new AngebotDecisionNotification(5, "ANG", 7, "K", true))));
        Assert.Contains(Sentence, Body(factory.CreateAngebotChangesRequested(new AngebotChangesRequestedNotification(5, "ANG", "c", 3), "i@example.invalid")));

        Assert.DoesNotContain(Sentence, Body(factory.CreateAngebotReady(new AngebotReadyNotification(5, "ANG", "K", "k@example.invalid", "tok"))));
        Assert.DoesNotContain(Sentence, Body(factory.CreateInvoiceReady(new InvoiceReadyNotification(9, "RE", "K", "k@example.invalid", 1m, new DateTime(2026, 8, 31), "tok"))));
    }

    /// <summary>S1-2 decision 4: no validity period is stated, because the record carries no ExpiresAt.</summary>
    [Fact]
    public void No_customer_template_states_a_link_validity_period()
    {
        var factory = CreateFactory();

        foreach (var body in new[]
        {
            Body(factory.CreateAngebotReady(new AngebotReadyNotification(5, "ANG", "K", "k@example.invalid", "tok"))),
            Body(factory.CreateInvoiceReady(new InvoiceReadyNotification(9, "RE", "K", "k@example.invalid", 1m, new DateTime(2026, 8, 31), "tok"))),
        })
        {
            Assert.DoesNotContain("gültig", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tage", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>G-4/G-5: none of this exists in the system, so none of it may appear in the copy.</summary>
    [Fact]
    public void InvoiceReady_carries_no_bank_details_payment_instruction_vat_rate_or_attachment()
    {
        var message = CreateFactory().CreateInvoiceReady(
            new InvoiceReadyNotification(9, "RE-2026-00009", "Familie Klein", "klein@example.invalid", 1234.56m, new DateTime(2026, 8, 31), "tok"));

        var body = Body(message);

        Assert.DoesNotContain("IBAN", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BIC", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("überweis", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MwSt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anhang", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public void Admin_templates_address_every_configured_recipient()
    {
        var message = CreateFactory().CreateNewWebsiteLead(
            new NewWebsiteLeadNotification(7, "Familie Klein", "0176", "klein@example.invalid"));

        Assert.Equal(
            ["office@example.invalid", "owner@example.invalid"],
            message.To.Mailboxes.Select(mailbox => mailbox.Address));
    }

    [Fact]
    public void The_sender_identity_comes_from_configuration()
    {
        var message = CreateFactory().CreateNewWebsiteLead(
            new NewWebsiteLeadNotification(7, "K", "0176", "k@example.invalid"));

        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal("no-reply@example.invalid", from.Address);
        Assert.Equal("Beispiel Bau GmbH", from.Name);
    }

    [Fact]
    public void ReplyTo_is_absent_unless_configured()
    {
        Assert.Empty(CreateFactory().CreateNewWebsiteLead(new NewWebsiteLeadNotification(7, "K", "0176", "k@example.invalid")).ReplyTo);
    }

    [Fact]
    public void ReplyTo_is_set_when_configured()
    {
        var message = CreateFactory(replyTo: "buero@example.invalid")
            .CreateNewWebsiteLead(new NewWebsiteLeadNotification(7, "K", "0176", "k@example.invalid"));

        Assert.Equal("buero@example.invalid", Assert.Single(message.ReplyTo.Mailboxes).Address);
    }

    /// <summary>S1-2: plaintext only. No HTML part, no multipart/alternative.</summary>
    [Fact]
    public void Every_message_is_plaintext_only()
    {
        var factory = CreateFactory();

        var messages = new[]
        {
            factory.CreateNewWebsiteLead(new NewWebsiteLeadNotification(7, "K", "0176", "k@example.invalid")),
            factory.CreateAngebotReady(new AngebotReadyNotification(5, "ANG", "K", "k@example.invalid", "tok")),
        };

        foreach (var message in messages)
        {
            var part = Assert.IsType<TextPart>(message.Body);
            Assert.True(part.IsPlain);
            Assert.False(part.IsHtml);
        }
    }
}
