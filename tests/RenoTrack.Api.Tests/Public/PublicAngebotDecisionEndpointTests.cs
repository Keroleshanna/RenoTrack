using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Public;

/// <summary>
/// <c>POST /api/v1/public/angebote/{token}/decision</c> — SRS FR-6.3/FR-6.5, Sequence Diagram §6,
/// StateMachine.md §2.3 and §5.
/// </summary>
/// <remarks>
/// What only a real request against a real database can show: that the token row, the Angebot and
/// the Lead all actually land together, and that BR-4's asymmetry holds over HTTP — the same link
/// that is refused for a second decision still serves the read endpoint.
/// </remarks>
[Collection("Api")]
public sealed class PublicAngebotDecisionEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task A_customer_can_approve_and_all_three_aggregates_move_together()
    {
        var (token, angebotId, leadId) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", body.GetProperty("decision").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("decisionAt").ValueKind);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        Assert.Equal(AngebotStatus.CustomerApproved, (await context.Angebote.SingleAsync(a => a.Id == angebotId)).Status);
        Assert.Equal(LeadStatus.Won, (await context.Leads.SingleAsync(l => l.Id == leadId)).Status);
        Assert.NotNull((await context.TokenLinks.SingleAsync(t => t.Token == token)).UsedAt);
    }

    [Fact]
    public async Task A_customer_can_reject_and_the_lead_is_lost()
    {
        var (token, angebotId, leadId) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Reject" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        Assert.Equal(AngebotStatus.CustomerRejected, (await context.Angebote.SingleAsync(a => a.Id == angebotId)).Status);
        Assert.Equal(LeadStatus.Lost, (await context.Leads.SingleAsync(l => l.Id == leadId)).Status);
    }

    /// <summary>
    /// No login anywhere in this flow — the entire authorisation model is possession of the token
    /// (Architecture.md §7.2).
    /// </summary>
    [Fact]
    public async Task The_decision_requires_no_authorization_header()
    {
        var (token, _, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);
        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- BR-4 --------------------------------------------------------------

    /// <summary>
    /// The whole point of BR-4, over HTTP: the second decision is refused with 409, and the
    /// recorded outcome is untouched — a leaked or forwarded link cannot flip an answer.
    /// </summary>
    [Fact]
    public async Task A_second_decision_is_refused_and_the_first_outcome_stands()
    {
        var (token, angebotId, leadId) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        var second = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Reject" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(AngebotStatus.CustomerApproved, (await context.Angebote.SingleAsync(a => a.Id == angebotId)).Status);
        Assert.Equal(LeadStatus.Won, (await context.Leads.SingleAsync(l => l.Id == leadId)).Status);
    }

    /// <summary>
    /// BR-4's actual wording, end to end: single use restricts state-changing actions, and
    /// "viewing (read-only) remains allowed". The same link that just returned 409 must still
    /// serve the GET — this is the pair of assertions that makes the rule real rather than
    /// asserted twice in isolation.
    /// </summary>
    [Fact]
    public async Task A_used_link_is_refused_for_deciding_but_still_serves_the_read()
    {
        var (token, _, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        var secondDecision = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
        var read = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.Conflict, secondDecision.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Approved", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
    }

    // ---- Token validation --------------------------------------------------

    [Fact]
    public async Task An_unknown_token_is_a_not_found_that_leaks_the_token_nowhere()
    {
        const string token = "no-such-decision-token";
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);

        // The credential sits mid-path here, not at the end — the reason the redaction is keyed on
        // the route parameter rather than on a segment position.
        Assert.Equal(
            "/api/v1/public/angebote/{token}/decision",
            JsonDocument.Parse(raw).RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task An_expired_token_is_gone_and_leaks_the_token_nowhere()
    {
        var (token, _, _) = await SentAngebotAsync();
        await ExpireTokenAsync(token);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
    }

    /// <summary>An expired link must not be consumable — a rejected attempt leaves it unused.</summary>
    [Fact]
    public async Task An_expired_token_is_not_consumed_by_the_attempt()
    {
        var (token, _, _) = await SentAngebotAsync();
        await ExpireTokenAsync(token);
        using var anonymous = factory.CreateClient();

        await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Null((await context.TokenLinks.SingleAsync(t => t.Token == token)).UsedAt);
    }

    [Fact]
    public async Task An_unrecognised_decision_value_is_a_bad_request()
    {
        var (token, _, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Maybe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The deliberate FR-6.3 gap, pinned so it stays a decision rather than drifting: a reason sent
    /// by a client is not accepted into the contract. It is ignored by binding, never stored, and
    /// never echoed back — the alternative to storing it is refusing it, not silently keeping it.
    /// </summary>
    [Fact]
    public async Task A_rejection_reason_is_not_part_of_the_contract()
    {
        var (token, angebotId, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision",
            new { decision = "Reject", reason = "Zu teuer im Vergleich zum Wettbewerb." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Zu teuer", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("reason", raw, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Empty(await context.AngebotReviewComments.Where(c => c.AngebotId == angebotId).ToListAsync());
    }

    // ---- Helpers -----------------------------------------------------------

    private async Task ExpireTokenAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        await context.Database.ExecuteSqlAsync(
            $"UPDATE TokenLinks SET ExpiresAt = {DateTime.UtcNow.AddDays(-1)} WHERE Token = {token}");
    }

    private async Task<(string Token, int AngebotId, int LeadId)> SentAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var lead = Lead.Create("Decision lead", "0176 5550005", $"decision-{Guid.NewGuid():N}@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();

            leadId = lead.Id;
        }

        int angebotId;
        using (var inspector = await ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword))
        {
            var created = await inspector.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
            angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var section = await inspector.PostAsJsonAsync(
                $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1", sortOrder = 1 });
            var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
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

            await inspector.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        }

        using (var admin = await ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword))
        {
            await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
            var sent = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        }

        using var readScope = factory.Services.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var token = (await readContext.TokenLinks.SingleAsync(t => t.EntityId == angebotId)).Token;

        return (token, angebotId, leadId);
    }

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }
}
