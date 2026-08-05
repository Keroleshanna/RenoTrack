using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.CatalogItems;

/// <summary>
/// Catalog endpoints (Architecture.md §5.2, PermissionMatrix.md §6, Wireframes D2/F1).
/// </summary>
/// <remarks>
/// The Catalog is shared company-wide, so the authorization story here is purely role-based: both
/// roles read, only Admin curates, only Inspector uses save-as. No row belongs to a caller, so there
/// is no ownership dimension to test — which is itself worth pinning, since an accidental scope
/// would silently hide other people's Catalog entries.
/// </remarks>
[Collection("Api")]
public sealed class CatalogItemEndpointsTests(RenoTrackApiFactory factory)
{
    // ---- Read: both roles --------------------------------------------------

    [Fact]
    public async Task Both_roles_can_browse_the_catalog()
    {
        var marker = await SeedCatalogItemAsync("Fliesen verlegen");

        using var admin = await AdminClientAsync();
        using var inspector = await InspectorClientAsync();

        var asAdmin = await SearchAsync(admin, marker);
        var asInspector = await SearchAsync(inspector, marker);

        Assert.Equal(1, asAdmin.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, asInspector.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_supports_a_term_and_paging()
    {
        var marker = await SeedCatalogItemAsync("Alpha", "Beta", "Gamma");
        using var client = await InspectorClientAsync();

        var response = await client.GetAsync($"/api/v1/catalog-items?searchTerm={marker}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task An_out_of_range_page_size_is_a_bad_request()
    {
        using var client = await InspectorClientAsync();

        var response = await client.GetAsync("/api/v1/catalog-items?pageSize=99999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Admin curation ----------------------------------------------------

    [Fact]
    public async Task Admin_can_create_a_catalog_item()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/v1/catalog-items", new
        {
            title = "Bodenbelag trockengepresst",
            defaultUnitCode = "m2",
            suggestedUnitPrice = 82.25m,
            defaultSpecification = "Feinsteinzeug",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bodenbelag trockengepresst", body.GetProperty("title").GetString());
        Assert.Equal(82.25m, body.GetProperty("suggestedUnitPrice").GetDecimal());
        Assert.False(body.GetProperty("isRetired").GetBoolean());

        // Direct curation records no provenance — that is what distinguishes it from save-as.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("createdFromAngebotItemId").ValueKind);
    }

    [Fact]
    public async Task Inspector_cannot_create_a_catalog_item_directly()
    {
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsJsonAsync("/api/v1/catalog-items", new
        {
            title = "Not allowed",
            defaultUnitCode = "m2",
            suggestedUnitPrice = 1.00m,
            defaultSpecification = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_edit_a_catalog_item()
    {
        var id = await SeedSingleCatalogItemAsync("Before edit");
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/catalog-items/{id}", new
        {
            title = "After edit",
            defaultUnitCode = "Stk",
            suggestedUnitPrice = 5.50m,
            defaultSpecification = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("After edit", body.GetProperty("title").GetString());
        Assert.Equal(5.50m, body.GetProperty("suggestedUnitPrice").GetDecimal());
    }

    [Fact]
    public async Task Inspector_cannot_edit_a_catalog_item()
    {
        var id = await SeedSingleCatalogItemAsync("Not editable by inspector");
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PutAsJsonAsync($"/api/v1/catalog-items/{id}", new
        {
            title = "Nope",
            defaultUnitCode = "Stk",
            suggestedUnitPrice = 1.00m,
            defaultSpecification = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Retirement (BR-12) ------------------------------------------------

    /// <summary>
    /// Retirement affects discovery only: the row survives, keeping every AngebotItem's trace link
    /// valid (BR-8/BR-12), and remains usable as a direct reference (BR-14).
    /// </summary>
    [Fact]
    public async Task Admin_can_retire_an_item_and_it_disappears_from_the_picker()
    {
        var marker = await SeedCatalogItemAsync("To be retired");
        using var admin = await AdminClientAsync();

        var before = await SearchAsync(admin, marker);
        var id = before.GetProperty("items")[0].GetProperty("id").GetInt32();

        var response = await admin.PostAsync($"/api/v1/catalog-items/{id}/retire", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isRetired").GetBoolean());

        var after = await SearchAsync(admin, marker);
        Assert.Equal(0, after.GetProperty("totalCount").GetInt32());

        // The row is still there — retirement is not deletion.
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.NotNull(await context.CatalogItems.FindAsync(id));
    }

    [Fact]
    public async Task Inspector_cannot_retire_a_catalog_item()
    {
        var id = await SeedSingleCatalogItemAsync("Not retirable by inspector");
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/catalog-items/{id}/retire", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Retiring_an_unknown_item_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/catalog-items/999999/retire", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Save as catalog item (FR-4.10) ------------------------------------

    [Fact]
    public async Task Inspector_can_save_an_angebot_item_as_a_catalog_entry()
    {
        var (client, itemId) = await AngebotItemAsync();

        var response = await client.PostAsync(
            $"/api/v1/angebot-items/{itemId}/save-as-catalog-item", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Wände abbrechen", body.GetProperty("title").GetString());
        Assert.Equal("m2", body.GetProperty("defaultUnit").GetString());
        Assert.Equal(25.00m, body.GetProperty("suggestedUnitPrice").GetDecimal());
        Assert.Equal(itemId, body.GetProperty("createdFromAngebotItemId").GetInt32());
    }

    /// <summary>
    /// PermissionMatrix.md §3 marks save-as "F" — the Catalog is shared, so <b>any</b> Inspector may
    /// contribute, including one who has nothing to do with that Angebot. This is the test that would
    /// fail if someone "helpfully" added an ownership check.
    /// </summary>
    [Fact]
    public async Task Any_inspector_can_save_an_item_from_an_angebot_they_do_not_own()
    {
        var (owner, itemId) = await AngebotItemAsync();
        owner.Dispose();

        using var otherInspector = await SecondInspectorClientAsync();
        var response = await otherInspector.PostAsync(
            $"/api/v1/angebot-items/{itemId}/save-as-catalog-item", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_use_the_save_as_path()
    {
        var (client, itemId) = await AngebotItemAsync();
        client.Dispose();

        using var admin = await AdminClientAsync();
        var response = await admin.PostAsync(
            $"/api/v1/angebot-items/{itemId}/save-as-catalog-item", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Saving_an_unknown_item_is_a_not_found()
    {
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync(
            "/api/v1/angebot-items/999999/save-as-catalog-item", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog-items");

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

    private static async Task<JsonElement> SearchAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"/api/v1/catalog-items?searchTerm={term}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Seeds items carrying a run-unique marker in their titles, and returns it, so a search can be
    /// scoped to this test's own rows in a database shared with every other API test.
    /// </summary>
    private async Task<string> SeedCatalogItemAsync(params string[] titles)
    {
        var marker = $"MK{Guid.NewGuid():N}"[..10];

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        foreach (var title in titles)
        {
            context.CatalogItems.Add(CatalogItem.Create(
                $"{title} {marker}",
                Domain.ValueObjects.ItemUnit.SquareMeter(),
                Domain.ValueObjects.Money.FromExact(10.00m)));
        }

        await context.SaveChangesAsync();
        return marker;
    }

    private async Task<int> SeedSingleCatalogItemAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var item = CatalogItem.Create(
            $"{title} {Guid.NewGuid():N}"[..40],
            Domain.ValueObjects.ItemUnit.SquareMeter(),
            Domain.ValueObjects.Money.FromExact(10.00m));

        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();

        return item.Id;
    }

    /// <summary>Builds a real Angebot line item through the API, and returns its id.</summary>
    private async Task<(HttpClient Client, int ItemId)> AngebotItemAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

            var lead = Lead.Create("Catalog save-as lead", "0176 5550003", "catalog-saveas@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();

            leadId = lead.Id;
        }

        var client = await InspectorClientAsync();

        var angebot = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        var angebotId = (await angebot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await client.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var item = await client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
        {
            sectionId,
            catalogItemId = (int?)null,
            description = "Wände abbrechen",
            specification = "Ziegelwand",
            unitCode = "m2",
            quantity = 10m,
            unitPrice = 25.00m,
            vatRate = "Standard",
        });

        var itemId = (await item.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("item").GetProperty("id").GetInt32();

        return (client, itemId);
    }
}
