using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// <c>POST /api/v1/angebote/{id}/resend</c> — SRS FR-6.1a, <b>D99</b>. What only a real request
/// against a real database can show: the role gate, that the supersession and the replacement
/// actually reach the database together, and that the response never carries a credential.
/// </summary>
[Collection("Api")]
public sealed class ResendAngebotEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Admin_can_reissue_the_link_of_a_sent_angebot()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        var before = await CurrentTokenAsync(angebotId);

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sent", body.GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        // The old row is retired, not removed — and never marked used, so UsedAt keeps its single
        // meaning (BR-4).
        var links = await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .ToListAsync();

        Assert.Equal(2, links.Count);

        var superseded = Assert.Single(links, link => link.Token == before);
        Assert.True(superseded.ExpiresAt <= DateTime.UtcNow);
        Assert.Null(superseded.UsedAt);

        // The invariant: exactly one credential a customer could still use.
        var now = DateTime.UtcNow;
        Assert.Single(links, link => link.UsedAt is null && link.ExpiresAt > now);
    }

    /// <summary>
    /// The credential reaches the customer only by email. Asserted against raw JSON rather than a
    /// typed read, because a typed deserialisation cannot notice a field the contract should not
    /// have.
    /// </summary>
    [Fact]
    public async Task The_response_never_carries_a_token()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await CurrentTokenAsync(angebotId), raw, StringComparison.Ordinal);
    }

    /// <summary>D99 Q4: SentAt records the original send, not the latest re-issue.</summary>
    [Fact]
    public async Task Reissuing_does_not_move_sent_at()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        var first = await (await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null))
            .Content.ReadFromJsonAsync<JsonElement>();
        var sentAtBefore = first.GetProperty("sentAt").GetString();

        await Task.Delay(20);
        var second = await (await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(sentAtBefore, second.GetProperty("sentAt").GetString());
    }

    /// <summary>Re-issuing twice is legitimate: each supersedes the last, and one usable link remains.</summary>
    [Fact]
    public async Task Reissuing_twice_leaves_three_rows_and_one_usable_link()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);
        var second = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var now = DateTime.UtcNow;
        var links = await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .ToListAsync();

        Assert.Equal(3, links.Count);
        Assert.Single(links, link => link.UsedAt is null && link.ExpiresAt > now);
    }

    /// <summary>
    /// Every state except <c>Sent</c> refuses, and nothing is written. The three earliest states are
    /// driven through the real endpoints, so no test fabricates a state the workflow cannot reach.
    /// </summary>
    [Fact]
    public async Task An_angebot_that_was_never_sent_is_a_conflict_and_issues_no_link()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(0, await context.TokenLinks.CountAsync(t => t.EntityId == angebotId));
    }

    [Fact]
    public async Task A_draft_angebot_is_a_conflict()
    {
        var (angebotId, _) = await SubmittedAngebotWithLeadAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// BR-4: once the customer has decided, the link is finished and there is nothing to supersede.
    /// Also covers the decided Angebot states, which are unreachable any other way.
    /// </summary>
    [Fact]
    public async Task A_decided_angebot_is_a_conflict_and_leaves_the_used_link_alone()
    {
        var (angebotId, _) = await SentAngebotAsync();
        var token = await CurrentTokenAsync(angebotId);

        using (var anonymous = factory.CreateClient())
        {
            var decided = await anonymous.PostAsJsonAsync(
                $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        }

        using var admin = await AdminClientAsync();
        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var links = await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .ToListAsync();

        // No replacement was minted behind a decision that has already been recorded.
        var link = Assert.Single(links);
        Assert.NotNull(link.UsedAt);
    }

    /// <summary>PermissionMatrix §4 marks re-issuing Admin-only.</summary>
    [Fact]
    public async Task An_inspector_is_forbidden()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorized()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reissuing_an_unknown_angebot_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/angebote/999999/resend", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The re-issue is recorded against the Angebot, since SentAt does not carry it.</summary>
    [Fact]
    public async Task A_reissue_is_audited_against_the_angebot()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/angebote/{angebotId}/resend", content: null);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var entries = await context.AuditLogs
            .Where(a => a.EntityType == nameof(Angebot) && a.EntityId == angebotId)
            .ToListAsync();

        Assert.Contains(entries, entry => entry.Action == AuditAction.AngebotLinkReissued);
    }

    private async Task<string> CurrentTokenAsync(int angebotId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        return (await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstAsync()).Token;
    }

    private async Task<(int AngebotId, int LeadId)> SentAngebotAsync()
    {
        var (angebotId, leadId) = await ApprovedAngebotAsync();

        using var admin = await AdminClientAsync();
        var sent = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        return (angebotId, leadId);
    }

    // ---- Helpers -----------------------------------------------------------

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

    private Task<HttpClient> AdminClientAsync() =>
        ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    private async Task<int> SeedLeadReadyForAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create("Send angebot lead", "0176 5550003", $"send-{Guid.NewGuid():N}@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        lead.AssignInspector(inspectorId);
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        await context.SaveChangesAsync();

        return lead.Id;
    }

    private async Task<(int AngebotId, int LeadId)> SubmittedAngebotWithLeadAsync()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var client = await InspectorClientAsync();

        var created = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
        {
            sectionId,
            catalogItemId = (int?)null,
            description = "Wände abbrechen",
            specification = (string?)null,
            unitCode = "m2",
            quantity = 10m,
            unitPrice = 25.00m,
            vatRate = "Standard",
        });

        var submitted = await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        return (angebotId, leadId);
    }

    private async Task<int> SubmittedAngebotAsync() => (await SubmittedAngebotWithLeadAsync()).AngebotId;

    private async Task<(int AngebotId, int LeadId)> ApprovedAngebotAsync()
    {
        var (angebotId, leadId) = await SubmittedAngebotWithLeadAsync();

        using var admin = await AdminClientAsync();
        var approved = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        return (angebotId, leadId);
    }
}
