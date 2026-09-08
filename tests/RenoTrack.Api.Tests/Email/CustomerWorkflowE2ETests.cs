using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;
using RenoTrack.Tests.Shared;

namespace RenoTrack.Api.Tests.Email;

/// <summary>
/// The customer workflow across every real boundary it actually crosses: the API, the notification
/// pipeline, MailKit's SMTP transport, a real socket, the captured message, and back in through the
/// anonymous decision endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>The link is taken from the email, never from the database.</b> That is the whole point. Every
/// other test in this suite reads the token out of <c>TokenLinks</c> because it is testing something
/// else; doing that here would skip the two things most likely to be wrong in a real deployment —
/// whether an email is actually sent, and whether the URL inside it is one a customer can open.
/// A test that fetched the token from the database would still pass with the mail transport broken.
/// </para>
/// <para>
/// <b>No Dashboard, no browser.</b> Those are Checkpoint 3's, on a machine with Mailpit. This is the
/// part that can be permanent: it runs in CI's existing Windows job, needs no container, no
/// external SMTP server and no new infrastructure.
/// </para>
/// <para>
/// <b>Deterministic by construction, with no sleeps and no polling.</b> `SmtpEmailSender` awaits
/// MailKit's `SendAsync`, which returns only after the server has answered `250` — and the server
/// records the message before writing that line. So when the HTTP response arrives, the message is
/// already captured. The delivery row is written inside the same request for the same reason.
/// </para>
/// <para>
/// Its own factory and its own database, because the host must exist only after the SMTP listener
/// has a port and must have email genuinely enabled — neither of which the shared collection
/// fixture can offer.
/// </para>
/// </remarks>
public sealed partial class CustomerWorkflowE2ETests : IAsyncLifetime
{
    private InProcessSmtpServer _smtp = null!;
    private CustomerWorkflowE2EFactory _factory = null!;

    public async Task InitializeAsync()
    {
        // Order matters: the listener picks a free port, and the host needs that port in its
        // configuration before it starts.
        _smtp = new InProcessSmtpServer();
        _factory = new CustomerWorkflowE2EFactory(_smtp.Port);
        await _factory.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _smtp.DisposeAsync();
    }

    // ---- The whole workflow, in one run ------------------------------------

    /// <summary>
    /// Send → real SMTP → captured mail → the customer's own link → decision → persisted state.
    /// </summary>
    [Fact]
    public async Task A_customer_approves_from_the_link_in_the_email_and_every_aggregate_moves()
    {
        var (angebotId, leadId) = await ApprovedAngebotAsync();

        // ---- Send, over the real transport ---------------------------------
        using var admin = await AdminClientAsync();
        var sent = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        // (1) The real SMTP transport ran: a socket was accepted and a message delivered over it.
        Assert.Equal(1, _smtp.SessionCount);
        var message = Assert.Single(_smtp.Messages);
        Assert.Contains(_smtp.Commands, c => c.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase));

        // (2) and (3) The customer's link is in the message, and comes out of the message.
        var customerUrl = ExtractCustomerUrl(message);
        Assert.StartsWith($"{CustomerWorkflowE2EFactory.PublicBaseUrl}/angebot/", customerUrl, StringComparison.Ordinal);

        var token = customerUrl[$"{CustomerWorkflowE2EFactory.PublicBaseUrl}/angebot/".Length..];
        Assert.NotEmpty(token);

        // The credential in the email is the real one. Read *after* extracting, as a check on the
        // email's contents — never as the way the test obtains it.
        await using (var verifyScope = _factory.Services.CreateAsyncScope())
        {
            var verify = verifyScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var issued = await verify.TokenLinks.SingleAsync(t => t.EntityId == angebotId);
            Assert.Equal(issued.Token, token);
        }

        // ---- (4) The decision, made with the captured credential -----------
        using var anonymous = _factory.CreateClient();
        var decision = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        var body = await decision.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", body.GetProperty("decision").GetString());

        // ---- (5) (6) (7) Persisted state -----------------------------------
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var tokenLink = await context.TokenLinks.SingleAsync(t => t.EntityId == angebotId);
        Assert.NotNull(tokenLink.UsedAt);

        var angebot = await context.Angebote.SingleAsync(a => a.Id == angebotId);
        Assert.Equal(AngebotStatus.CustomerApproved, angebot.Status);
        Assert.NotNull(angebot.DecisionAt);

        var lead = await context.Leads.SingleAsync(l => l.Id == leadId);
        Assert.Equal(LeadStatus.Won, lead.Status);

        // ---- (8) Notification deliveries -----------------------------------
        // Two mails: the customer's link, and the Admin's decision notification. Both recorded as
        // actually Sent, which is the difference between "we tried" and "it went".
        var deliveries = await context.NotificationDeliveries
            .Where(d => d.EntityId == angebotId)
            .ToListAsync();

        var ready = Assert.Single(deliveries, d => d.NotificationType == NotificationType.AngebotReady);
        Assert.Equal(NotificationDeliveryStatus.Sent, ready.Status);
        Assert.NotNull(ready.SentAt);

        var decided = Assert.Single(deliveries, d => d.NotificationType == NotificationType.AngebotDecision);
        Assert.Equal(NotificationDeliveryStatus.Sent, decided.Status);
        Assert.Equal(CustomerWorkflowE2EFactory.AdminNotificationRecipient, decided.Recipient);

        // The decision really did produce a second mail over the same real transport.
        Assert.Equal(2, _smtp.Messages.Count);

        // ---- (9) The credential reached no log -----------------------------
        AssertNoTokenInLogs(token);
    }

    /// <summary>
    /// The customer's rejection reason (FR-6.3, D98) survives the same round trip, and the Lead ends
    /// at <see cref="LeadStatus.Lost"/> rather than <see cref="LeadStatus.Won"/>.
    /// </summary>
    [Fact]
    public async Task A_customer_rejects_with_a_reason_from_the_emailed_link()
    {
        var (angebotId, leadId) = await ApprovedAngebotAsync();

        using var admin = await AdminClientAsync();
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        var token = TokenFrom(Assert.Single(_smtp.Messages));

        const string reason = "Preis liegt über unserem Budget für die Größe der Fläche.";
        using var anonymous = _factory.CreateClient();
        var decision = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Reject", reason });

        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var angebot = await context.Angebote.SingleAsync(a => a.Id == angebotId);
        Assert.Equal(AngebotStatus.CustomerRejected, angebot.Status);
        Assert.Equal(reason, angebot.DecisionReason);

        var lead = await context.Leads.SingleAsync(l => l.Id == leadId);
        Assert.Equal(LeadStatus.Lost, lead.Status);

        AssertNoTokenInLogs(token);
    }

    /// <summary>
    /// BR-4: the link is single use <em>for decisions</em>. The second attempt with the same
    /// captured credential is refused, nothing changes, and no second customer-decision mail goes
    /// out — an established contract, re-proven here because this is the path a customer who
    /// double-clicks actually takes.
    /// </summary>
    [Fact]
    public async Task A_second_decision_through_the_same_emailed_link_is_refused()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();

        using var admin = await AdminClientAsync();
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        var token = TokenFrom(Assert.Single(_smtp.Messages));

        using var anonymous = _factory.CreateClient();
        var first = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var mailsAfterFirst = _smtp.Messages.Count;

        var second = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Reject" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var angebot = await context.Angebote.SingleAsync(a => a.Id == angebotId);

        // Still the first answer, and no mail claiming otherwise.
        Assert.Equal(AngebotStatus.CustomerApproved, angebot.Status);
        Assert.Equal(mailsAfterFirst, _smtp.Messages.Count);
    }

    /// <summary>
    /// Viewing stays open after a decision (BR-4, PermissionMatrix §7) — the customer who keeps the
    /// email can still read what they agreed to.
    /// </summary>
    [Fact]
    public async Task The_emailed_link_still_renders_the_quote_after_the_decision()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();

        using var admin = await AdminClientAsync();
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        var token = TokenFrom(Assert.Single(_smtp.Messages));

        using var anonymous = _factory.CreateClient();
        await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        var view = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var body = await view.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", body.GetProperty("decision").GetString());
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>
    /// Parses the captured DATA payload as a real MIME message and reads the customer's URL out of
    /// its decoded body.
    /// </summary>
    /// <remarks>
    /// <b>Parsed, not pattern-matched against the raw payload.</b> A token is 43 URL-safe base64
    /// characters, so the link is long enough for the transfer encoding to soft-wrap it — and a
    /// regex over the raw DATA would then extract a truncated token and fail for a reason that has
    /// nothing to do with the workflow. Decoding with MimeKit is also what a mail client does, so
    /// this reads the link the customer would actually see.
    /// </remarks>
    private static string ExtractCustomerUrl(string capturedMessage)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(capturedMessage));
        var mime = MimeMessage.Load(stream);

        var text = mime.TextBody
            ?? throw new InvalidOperationException("The captured message carried no text body.");

        var match = CustomerLink().Match(text);
        Assert.True(match.Success, $"No customer link found in the email body:{Environment.NewLine}{text}");

        return match.Value;
    }

    private static string TokenFrom(string capturedMessage) =>
        ExtractCustomerUrl(capturedMessage)[$"{CustomerWorkflowE2EFactory.PublicBaseUrl}/angebot/".Length..];

    [GeneratedRegex(@"https://kunde\.example\.test/angebot/[A-Za-z0-9_\-]+")]
    private static partial Regex CustomerLink();

    /// <summary>
    /// The token must not be in any log line, at any level, from any category.
    /// </summary>
    /// <remarks>
    /// The two categories that would leak it — ASP.NET's hosting diagnostics and
    /// <c>IHttpClientFactory</c>'s handlers — log at Information, which is why the factory captures
    /// from Information down. Capturing only warnings would make this assertion unable to fail.
    /// </remarks>
    private void AssertNoTokenInLogs(string token)
    {
        var offenders = _factory.Logs.Messages
            .Where(m => m.Contains(token, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"The customer's token appeared in {offenders.Count} log message(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    private Task<HttpClient> AdminClientAsync() =>
        ClientAsync(CustomerWorkflowE2EFactory.AdminEmail, CustomerWorkflowE2EFactory.AdminPassword);

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(CustomerWorkflowE2EFactory.InspectorEmail, CustomerWorkflowE2EFactory.InspectorPassword);

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    /// <summary>
    /// A Lead through to an internally approved Angebot, driven through the real endpoints rather
    /// than seeded — the states this workflow depends on are the ones the API produces.
    /// </summary>
    private async Task<(int AngebotId, int LeadId)> ApprovedAngebotAsync()
    {
        var inspectorId = await _factory.GetUserIdAsync(CustomerWorkflowE2EFactory.InspectorEmail);

        int leadId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

            var lead = Lead.Create(
                "E2E Kundin",
                "0176 5550111",
                $"kundin-{Guid.NewGuid():N}@example.test",
                LeadSource.Phone);

            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();

            leadId = lead.Id;
        }

        using var inspector = await InspectorClientAsync();

        var created = await inspector.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await inspector.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
        {
            sectionId,
            catalogItemId = (int?)null,
            description = "Wände spachteln",
            specification = (string?)null,
            unitCode = "m2",
            quantity = 25m,
            unitPrice = 40.00m,
            vatRate = "Standard",
        });

        var submitted = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        // Submitting notifies the Admin, over the same real transport. Cleared so each test's
        // assertions are about the customer mails it is actually driving.
        using var admin = await AdminClientAsync();
        var approved = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        _smtp.Reset();

        return (angebotId, leadId);
    }
}
