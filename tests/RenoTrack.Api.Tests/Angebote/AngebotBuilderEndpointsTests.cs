using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// The Angebot builder endpoints (Architecture.md §5.2, PermissionMatrix.md §3): create a draft, add
/// and remove sections and items, and read the Angebot back.
/// </summary>
/// <remarks>
/// Per D58 this asserts what the API layer adds over the layers beneath it — routing, model binding,
/// role and ownership enforcement reaching from a real JWT to a real 403, and the shape that goes on
/// the wire. Business rules already covered exhaustively by Domain and Application tests (totals
/// arithmetic, the transition matrix) are deliberately not re-verified here; what <em>is</em>
/// verified is that the edit-lock and the ownership rules survive the trip over HTTP.
/// </remarks>
[Collection("Api")]
public sealed class AngebotBuilderEndpointsTests(RenoTrackApiFactory factory)
{
    // ---- Create -----------------------------------------------------------

    [Fact]
    public async Task Owning_inspector_can_create_a_draft_angebot_for_their_lead()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var client = await InspectorClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(leadId, body.GetProperty("leadId").GetInt32());

        // Enums serialize as names, not ordinals (D61).
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.StartsWith("ANG-", body.GetProperty("angebotNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal(0m, body.GetProperty("netTotal").GetDecimal());

        // The Location header points at the read endpoint this slice adds.
        Assert.NotNull(response.Headers.Location);
    }

    /// <summary>PermissionMatrix.md §3 grants Admin nothing for creating a draft.</summary>
    [Fact]
    public async Task Admin_cannot_create_a_draft_angebot()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_create_an_angebot_on_another_inspectors_lead()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var client = await SecondInspectorClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>StateMachine.md §2.4: only one non-terminal Angebot per Lead.</summary>
    [Fact]
    public async Task A_second_active_angebot_for_the_same_lead_is_rejected_as_a_conflict()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var client = await InspectorClientAsync();

        var first = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---- Sections and items -----------------------------------------------

    [Fact]
    public async Task Sections_and_items_can_be_added_and_totals_come_back_updated()
    {
        var (client, angebotId) = await DraftAngebotAsync();

        var sectionResponse = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1 Abbruch", sortOrder = 1 });

        Assert.Equal(HttpStatusCode.Created, sectionResponse.StatusCode);
        var sectionId = (await sectionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var itemResponse = await client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
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

        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);

        var body = await itemResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(250.00m, body.GetProperty("item").GetProperty("lineTotal").GetDecimal());
        Assert.Equal(250.00m, body.GetProperty("summary").GetProperty("netTotal").GetDecimal());
        Assert.Equal(297.50m, body.GetProperty("summary").GetProperty("grossTotal").GetDecimal());
    }

    [Fact]
    public async Task An_inspector_cannot_add_a_section_to_another_inspectors_angebot()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Not mine", sortOrder = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>PermissionMatrix.md §3 marks Admin "R" for a draft — they may read it, never edit it.</summary>
    [Fact]
    public async Task Admin_cannot_add_a_section_to_a_draft()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Admin edit", sortOrder = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Removal (PermissionMatrix.md §3, "Add/remove Sections & Items") ----

    [Fact]
    public async Task Removing_an_item_leaves_its_section_and_updates_totals()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        var (sectionId, itemId) = await AddSectionWithItemAsync(client, angebotId);

        var response = await client.DeleteAsync($"/api/v1/angebote/{angebotId}/items/{itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("netTotal").GetDecimal());

        var detail = await ReadAngebotAsync(client, angebotId);
        var section = Assert.Single(detail.GetProperty("sections").EnumerateArray());
        Assert.Equal(sectionId, section.GetProperty("id").GetInt32());
        Assert.Empty(section.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Removing_a_section_removes_its_items_too()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        var (sectionId, _) = await AddSectionWithItemAsync(client, angebotId);

        var response = await client.DeleteAsync($"/api/v1/angebote/{angebotId}/sections/{sectionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await ReadAngebotAsync(client, angebotId);
        Assert.Empty(detail.GetProperty("sections").EnumerateArray());
        Assert.Equal(0m, detail.GetProperty("netTotal").GetDecimal());
    }

    [Fact]
    public async Task Removing_an_unknown_item_is_a_not_found()
    {
        var (client, angebotId) = await DraftAngebotAsync();

        var response = await client.DeleteAsync($"/api/v1/angebote/{angebotId}/items/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_remove_a_section_from_another_inspectors_angebot()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        var (sectionId, _) = await AddSectionWithItemAsync(client, angebotId);
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.DeleteAsync($"/api/v1/angebote/{angebotId}/sections/{sectionId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The edit-lock reaching all the way out to a status code: StateMachine.md §2.4 locks the tree
    /// once the Angebot leaves Draft, and D59 maps the Domain's <c>InvalidOperationException</c> to
    /// 409. Submission is done directly against the aggregate because its endpoint is Slice 2.
    /// </summary>
    [Fact]
    public async Task Editing_a_submitted_angebot_is_rejected_as_a_conflict()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        var (sectionId, itemId) = await AddSectionWithItemAsync(client, angebotId);
        await SubmitDirectlyAsync(angebotId);

        var addSection = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Too late", sortOrder = 2 });
        var removeSection = await client.DeleteAsync($"/api/v1/angebote/{angebotId}/sections/{sectionId}");
        var removeItem = await client.DeleteAsync($"/api/v1/angebote/{angebotId}/items/{itemId}");

        Assert.Equal(HttpStatusCode.Conflict, addSection.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeSection.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removeItem.StatusCode);
    }

    // ---- Reads -------------------------------------------------------------

    [Fact]
    public async Task Reading_an_angebot_returns_the_tree_with_totals_and_vat_breakdown()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        await AddSectionWithItemAsync(client, angebotId);

        var detail = await ReadAngebotAsync(client, angebotId);

        Assert.Equal(angebotId, detail.GetProperty("id").GetInt32());
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.Equal(250.00m, detail.GetProperty("netTotal").GetDecimal());

        var vat = Assert.Single(detail.GetProperty("vatBreakdown").EnumerateArray());
        Assert.Equal("Standard", vat.GetProperty("rate").GetString());
        Assert.Equal(47.50m, vat.GetProperty("vatAmount").GetDecimal());

        var section = Assert.Single(detail.GetProperty("sections").EnumerateArray());
        var item = Assert.Single(section.GetProperty("items").EnumerateArray());
        Assert.Equal("Wände abbrechen", item.GetProperty("description").GetString());
    }

    /// <summary>Admin is "F" for viewing (PermissionMatrix.md §4) even though they cannot edit.</summary>
    [Fact]
    public async Task Admin_can_read_an_angebot_they_do_not_own()
    {
        var (client, angebotId) = await DraftAngebotAsync();
        await AddSectionWithItemAsync(client, angebotId);

        using var admin = await AdminClientAsync();
        var response = await admin.GetAsync($"/api/v1/angebote/{angebotId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_read_another_inspectors_angebot()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.GetAsync($"/api/v1/angebote/{angebotId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reading_an_unknown_angebot_is_a_not_found_problem_details()
    {
        using var client = await InspectorClientAsync();

        var response = await client.GetAsync("/api/v1/angebote/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task The_lead_angebot_list_is_scoped_to_the_requesting_inspector()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var owner = await InspectorClientAsync();
        var created = await owner.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var admin = await AdminClientAsync();
        using var other = await SecondInspectorClientAsync();

        var asOwner = await ReadListAsync(owner, leadId);
        var asAdmin = await ReadListAsync(admin, leadId);
        var asOther = await ReadListAsync(other, leadId);

        Assert.Single(asOwner);
        Assert.Single(asAdmin);
        Assert.Empty(asOther);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/angebote/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    /// <summary>
    /// A Lead assigned to the seeded Inspector and already through its Inspection, which is
    /// <c>Lead.MarkAngebotInProgress()</c>'s precondition. Seeded directly rather than through the
    /// API so this class does not depend on the Inspection endpoints' behaviour.
    /// </summary>
    private async Task<int> SeedLeadReadyForAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create(
            "Angebot builder lead", "0176 5550001", "angebot-builder@example.com", LeadSource.Phone);

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

    private static async Task<(int SectionId, int ItemId)> AddSectionWithItemAsync(HttpClient client, int angebotId)
    {
        var sectionResponse = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1 Abbruch", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, sectionResponse.StatusCode);

        var sectionId = (await sectionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var itemResponse = await client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
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
        Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);

        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("item").GetProperty("id").GetInt32();

        return (sectionId, itemId);
    }

    private static async Task<JsonElement> ReadAngebotAsync(HttpClient client, int angebotId)
    {
        var response = await client.GetAsync($"/api/v1/angebote/{angebotId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> ReadListAsync(HttpClient client, int leadId)
    {
        var response = await client.GetAsync($"/api/v1/leads/{leadId}/angebote");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.EnumerateArray()];
    }

    /// <summary>
    /// Submits straight through the aggregate: the submit endpoint arrives in Slice 2, and this
    /// class only needs the resulting locked state.
    /// </summary>
    private async Task SubmitDirectlyAsync(int angebotId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var angebot = await context.Angebote
            .Include(a => a.Sections)
            .ThenInclude(s => s.Items)
            .SingleAsync(a => a.Id == angebotId);

        angebot.SubmitForReview();
        await context.SaveChangesAsync();
    }
}
