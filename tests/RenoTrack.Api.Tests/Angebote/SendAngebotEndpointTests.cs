using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// <c>POST /api/v1/angebote/{id}/send</c> — SRS FR-6.1, Sequence Diagram §6. What this class adds
/// over the Application-layer tests is what only a real request can show: the role gate, and that
/// the Angebot transition, the Lead transition and the token row all actually reach the database
/// together.
/// </summary>
[Collection("Api")]
public sealed class SendAngebotEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Admin_can_send_an_internally_approved_angebot()
    {
        var (angebotId, leadId) = await ApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sent", body.GetProperty("status").GetString());
        Assert.NotNull(body.GetProperty("sentAt").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        // The Lead moved too — StateMachine.md §1.3. This is what finally makes LeadStatus.AngebotSent
        // reachable, and therefore what Phase 6's decision endpoint will depend on.
        var lead = await context.Leads.SingleAsync(l => l.Id == leadId);
        Assert.Equal(LeadStatus.AngebotSent, lead.Status);

        // And the token link is really there, unused, pointing at this Angebot.
        var tokenLink = await context.TokenLinks.SingleAsync(t => t.EntityId == angebotId);
        Assert.Equal(TokenLinkEntityType.Angebot, tokenLink.EntityType);
        Assert.Null(tokenLink.UsedAt);
        Assert.True(tokenLink.ExpiresAt > DateTime.UtcNow);
    }

    /// <summary>
    /// The token must never appear in the response. It is a customer credential delivered by email;
    /// echoing it to the sender would put it in logs, proxies and browser history. Asserted against
    /// the raw JSON rather than a DTO property, so a future field carrying it would still be caught.
    /// </summary>
    [Fact]
    public async Task The_response_never_carries_the_token()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
        var raw = await response.Content.ReadAsStringAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var token = (await context.TokenLinks.SingleAsync(t => t.EntityId == angebotId)).Token;

        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        Assert.Null(response.Headers.Location);
    }

    /// <summary>
    /// PermissionMatrix.md §4 marks "Send Angebot to Lead" Admin "F". The empty-body assertion is
    /// what distinguishes this role-gate rejection from an ownership <c>ForbiddenException</c>,
    /// which would carry ProblemDetails — without it the test would pass even if the role
    /// requirement were removed (the Phase 4 Slice 9 finding).
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_send_even_their_own_angebot()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>StateMachine.md §2.3: only <c>ApprovedInternally</c> may be sent (BR-1).</summary>
    [Fact]
    public async Task Sending_an_angebot_still_in_review_is_a_conflict()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>Sending twice must not mint a second link — BR-4 would be meaningless otherwise.</summary>
    [Fact]
    public async Task Sending_twice_is_a_conflict_and_issues_no_second_token()
    {
        var (angebotId, _) = await ApprovedAngebotAsync();
        using var admin = await AdminClientAsync();
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        var second = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(1, await context.TokenLinks.CountAsync(t => t.EntityId == angebotId));
    }

    [Fact]
    public async Task Sending_an_unknown_angebot_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/angebote/999999/send", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
