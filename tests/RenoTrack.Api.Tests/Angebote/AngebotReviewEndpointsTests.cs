using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// The internal review loop (SRS FR-5.1–5.4, StateMachine.md §2.3, PermissionMatrix.md §4):
/// submit, approve, request changes, and read the comment history.
/// </summary>
/// <remarks>
/// The authorization asymmetry is what this class exists to pin. Submitting is Inspector "S", so a
/// non-owning Inspector is refused; approving and requesting changes are Admin "F", so <b>any</b>
/// Admin may act on <b>any</b> Angebot and no ownership check exists at all. Getting that backwards
/// in either direction would be invisible to Domain and Application tests of the handlers alone.
/// </remarks>
[Collection("Api")]
public sealed class AngebotReviewEndpointsTests(RenoTrackApiFactory factory)
{
    // ---- Submit ------------------------------------------------------------

    [Fact]
    public async Task Owning_inspector_can_submit_a_draft_with_at_least_one_item()
    {
        var (client, angebotId) = await DraftWithItemAsync();

        var response = await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InReview", body.GetProperty("status").GetString());
    }

    /// <summary>The aggregate's own guard: no section with an item means nothing to review.</summary>
    [Fact]
    public async Task Submitting_an_empty_draft_is_rejected_as_a_conflict()
    {
        var (client, angebotId) = await DraftAngebotAsync();

        var response = await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_submit_another_inspectors_angebot()
    {
        var (_, angebotId) = await DraftWithItemAsync();
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_submit_an_angebot()
    {
        var (_, angebotId) = await DraftWithItemAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Approve -----------------------------------------------------------

    [Fact]
    public async Task Admin_can_approve_a_submitted_angebot()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ApprovedInternally", body.GetProperty("status").GetString());

        // The reviewing Admin is recorded from the token, not the body (D61).
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        Assert.Equal(adminId, body.GetProperty("reviewedByAdminId").GetInt32());
    }

    /// <summary>
    /// PermissionMatrix.md §4 marks approval "F" — the review gate is a role privilege, not an
    /// ownership one, so an Inspector is refused even for their own Angebot.
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_approve_even_their_own_angebot()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approving_a_draft_that_was_never_submitted_is_a_conflict()
    {
        var (_, angebotId) = await DraftWithItemAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Request changes ---------------------------------------------------

    [Fact]
    public async Task Admin_can_request_changes_and_the_comment_is_recorded()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/request-changes",
            new { comment = "Bitte Position 2 neu kalkulieren." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ChangesRequested", body.GetProperty("status").GetString());

        var comments = await ReadCommentsAsync(admin, angebotId);
        var comment = Assert.Single(comments);
        Assert.Equal("Bitte Position 2 neu kalkulieren.", comment.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task Requesting_changes_without_a_comment_is_a_bad_request()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/request-changes", new { comment = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// FR-5.3's loop, end to end: returned, edited, resubmitted. The <c>ChangesRequested → Draft</c>
    /// step has no endpoint — StateMachine.md §2.3 says it happens the moment editing resumes, and
    /// the aggregate implements exactly that. This test is the proof that it works over HTTP.
    /// </summary>
    [Fact]
    public async Task The_review_loop_can_repeat_editing_reopens_the_draft_without_an_endpoint()
    {
        var (client, angebotId) = await DraftWithItemAsync();
        await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        using var admin = await AdminClientAsync();
        await admin.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/request-changes", new { comment = "Rework." });

        // Editing resumes — no "reopen" call anywhere.
        var added = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 2", sortOrder = 2 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/angebote/{angebotId}");
        Assert.Equal("Draft", detail.GetProperty("status").GetString());

        var resubmitted = await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);

        var approved = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    // ---- Review comments ---------------------------------------------------

    [Fact]
    public async Task The_owning_inspector_can_read_the_review_history()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var admin = await AdminClientAsync();
        await admin.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/request-changes", new { comment = "First round." });

        using var inspector = await InspectorClientAsync();
        var comments = await ReadCommentsAsync(inspector, angebotId);

        Assert.Equal("First round.", Assert.Single(comments).GetProperty("comment").GetString());
    }

    [Fact]
    public async Task An_inspector_cannot_read_another_inspectors_review_history()
    {
        var angebotId = await SubmittedAngebotAsync();
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.GetAsync($"/api/v1/angebote/{angebotId}/review-comments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Review_history_for_an_unknown_angebot_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/angebote/999999/review-comments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

    private Task<HttpClient> SecondInspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.SecondInspectorEmail, RenoTrackApiFactory.SecondInspectorPassword);

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

        var lead = Lead.Create("Angebot review lead", "0176 5550002", "angebot-review@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        lead.AssignInspector(inspectorId);
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        await context.SaveChangesAsync();

        return lead.Id;
    }

    private async Task<(HttpClient Client, int AngebotId)> DraftAngebotAsync()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        var client = await InspectorClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (client, body.GetProperty("id").GetInt32());
    }

    private async Task<(HttpClient Client, int AngebotId)> DraftWithItemAsync()
    {
        var (client, angebotId) = await DraftAngebotAsync();

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

        return (client, angebotId);
    }

    private async Task<int> SubmittedAngebotAsync()
    {
        var (client, angebotId) = await DraftWithItemAsync();

        using (client)
        {
            var response = await client.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return angebotId;
    }

    private static async Task<List<JsonElement>> ReadCommentsAsync(HttpClient client, int angebotId)
    {
        var response = await client.GetAsync($"/api/v1/angebote/{angebotId}/review-comments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.EnumerateArray()];
    }
}
