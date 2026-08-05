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
/// <c>POST /api/v1/angebote/{id}/duplicate</c> (SRS FR-4.11). Inspector only, and scoped twice over:
/// the source must be theirs and so must the target Lead.
/// </summary>
[Collection("Api")]
public sealed class DuplicateAngebotEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Duplicating_produces_a_new_draft_on_the_target_lead_with_the_same_tree()
    {
        var (client, sourceId) = await SourceAngebotAsync();
        var targetLeadId = await SeedLeadReadyForAngebotAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var duplicateId = body.GetProperty("id").GetInt32();

        Assert.NotEqual(sourceId, duplicateId);
        Assert.Equal(targetLeadId, body.GetProperty("leadId").GetInt32());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal(250.00m, body.GetProperty("netTotal").GetDecimal());

        var source = await ReadAsync(client, sourceId);
        var duplicate = await ReadAsync(client, duplicateId);

        Assert.NotEqual(
            source.GetProperty("angebotNumber").GetString(),
            duplicate.GetProperty("angebotNumber").GetString());

        var sourceSection = Assert.Single(source.GetProperty("sections").EnumerateArray());
        var duplicateSection = Assert.Single(duplicate.GetProperty("sections").EnumerateArray());

        Assert.Equal(
            sourceSection.GetProperty("title").GetString(),
            duplicateSection.GetProperty("title").GetString());

        var duplicateItem = Assert.Single(duplicateSection.GetProperty("items").EnumerateArray());
        Assert.Equal("Wände abbrechen", duplicateItem.GetProperty("description").GetString());
        Assert.Equal(250.00m, duplicateItem.GetProperty("lineTotal").GetDecimal());
    }

    /// <summary>
    /// BR-8 makes <c>CatalogItemId</c> traceability only, so the copy keeps it — the line genuinely
    /// did originate from that Catalog entry, and nothing about the copy depends on the Catalog's
    /// current state.
    /// </summary>
    [Fact]
    public async Task Duplicating_preserves_the_catalog_traceability_link()
    {
        var catalogItemId = await SeedCatalogItemAsync();
        var (client, sourceId) = await SourceAngebotAsync(catalogItemId);
        var targetLeadId = await SeedLeadReadyForAngebotAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var duplicateId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var duplicate = await ReadAsync(client, duplicateId);

        var item = Assert.Single(
            Assert.Single(duplicate.GetProperty("sections").EnumerateArray()).GetProperty("items").EnumerateArray());

        Assert.Equal(catalogItemId, item.GetProperty("catalogItemId").GetInt32());
    }

    [Fact]
    public async Task An_inspector_cannot_duplicate_another_inspectors_angebot()
    {
        var (owner, sourceId) = await SourceAngebotAsync();
        owner.Dispose();

        var targetLeadId = await SeedLeadReadyForAngebotAsync();
        using var intruder = await SecondInspectorClientAsync();

        var response = await intruder.PostAsJsonAsync(
            $"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_duplicate_onto_a_lead_they_do_not_own()
    {
        var (client, sourceId) = await SourceAngebotAsync();
        var foreignLeadId = await SeedLeadReadyForAngebotAsync(RenoTrackApiFactory.SecondInspectorEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId = foreignLeadId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>StateMachine.md §2.4 applies to the target exactly as it does to a fresh creation.</summary>
    [Fact]
    public async Task Duplicating_onto_a_lead_that_already_has_an_active_angebot_is_a_conflict()
    {
        var (client, sourceId) = await SourceAngebotAsync();
        var targetLeadId = await SeedLeadReadyForAngebotAsync();

        var first = await client.PostAsJsonAsync($"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_duplicate_an_angebot()
    {
        var (client, sourceId) = await SourceAngebotAsync();
        client.Dispose();

        var targetLeadId = await SeedLeadReadyForAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/angebote/{sourceId}/duplicate", new { targetLeadId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Duplicating_an_unknown_angebot_is_a_not_found()
    {
        var targetLeadId = await SeedLeadReadyForAngebotAsync();
        using var client = await InspectorClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/angebote/999999/duplicate", new { targetLeadId });

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

    private async Task<int> SeedLeadReadyForAngebotAsync(string? inspectorEmail = null)
    {
        var inspectorId = await factory.GetUserIdAsync(inspectorEmail ?? RenoTrackApiFactory.InspectorEmail);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create("Duplicate target", "0176 5550004", "duplicate@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        lead.AssignInspector(inspectorId);
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        await context.SaveChangesAsync();

        return lead.Id;
    }

    private async Task<int> SeedCatalogItemAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var item = CatalogItem.Create(
            $"Duplicate source {Guid.NewGuid():N}"[..40],
            Domain.ValueObjects.ItemUnit.SquareMeter(),
            Domain.ValueObjects.Money.FromExact(25.00m));

        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();

        return item.Id;
    }

    /// <summary>An Angebot with one section and one item, built through the API.</summary>
    private async Task<(HttpClient Client, int AngebotId)> SourceAngebotAsync(int? catalogItemId = null)
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        var client = await InspectorClientAsync();

        var created = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1 Abbruch", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var item = await client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
        {
            sectionId,
            catalogItemId,
            description = "Wände abbrechen",
            specification = "Ziegelwand",
            unitCode = "m2",
            quantity = 10m,
            unitPrice = 25.00m,
            vatRate = "Standard",
        });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);

        return (client, angebotId);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, int angebotId)
    {
        var response = await client.GetAsync($"/api/v1/angebote/{angebotId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
