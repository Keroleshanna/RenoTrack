using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Leads;

/// <summary>
/// The Lead read endpoints (Wireframe C1 detail, B2 pipeline). The security-relevant assertions
/// here matter more than the happy paths: an Inspector must see only their own Leads, must not be
/// able to widen that scope by asking, and an account with no role must be refused rather than
/// silently treated as unrestricted.
/// </summary>
[Collection("Api")]
public sealed class LeadReadEndpointsTests(RenoTrackApiFactory factory)
{
    // ---------- GET /leads/{id} ----------

    [Fact]
    public async Task Admin_can_read_any_lead()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: await InspectorIdAsync());
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.GetAsync($"/api/v1/leads/{leadId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(leadId, body.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Inspector_can_read_their_own_lead()
    {
        var inspectorId = await InspectorIdAsync();
        var leadId = await SeedLeadAsync(assignedInspectorId: inspectorId);
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await client.GetAsync($"/api/v1/leads/{leadId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_from_reading_another_inspectors_lead()
    {
        var otherInspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var leadId = await SeedLeadAsync(assignedInspectorId: otherInspectorId);
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await client.GetAsync($"/api/v1/leads/{leadId}");

        // 403 rather than 404: PermissionMatrix §1 marks this "S", which CLAUDE.md §16 maps to an
        // ownership failure, and D59 maps that to 403.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_missing_lead_returns_404()
    {
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.GetAsync("/api/v1/leads/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_lead_without_a_token_returns_401()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/leads/{leadId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- fail-secure authorization ----------

    [Fact]
    public async Task An_account_with_no_role_is_refused_rather_than_treated_as_unrestricted()
    {
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.NoRoleEmail, RenoTrackApiFactory.NoRolePassword);

        var single = await client.GetAsync("/api/v1/leads/1");
        var list = await client.GetAsync("/api/v1/leads");

        // The dangerous failure would be 200 with every Lead — an authenticated caller falling
        // through to "unrestricted" because they merely are not an Inspector.
        Assert.Equal(HttpStatusCode.Forbidden, single.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Role_claims_are_actually_enforced_by_Authorize_attributes()
    {
        // Proves role claims survive issuance and validation. If they did not, IsInRole would be
        // false everywhere and every scoping decision in this controller would silently fail open.
        using var inspector = await AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);
        using var admin = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var inspectorResponse = await inspector.GetAsync("/api/v1/leads");
        var adminResponse = await admin.GetAsync("/api/v1/leads");

        Assert.Equal(HttpStatusCode.OK, inspectorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    // ---------- GET /leads ----------

    [Fact]
    public async Task Admin_sees_leads_assigned_to_any_inspector()
    {
        var inspectorId = await InspectorIdAsync();
        var otherId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var mine = await SeedLeadAsync(assignedInspectorId: inspectorId);
        var theirs = await SeedLeadAsync(assignedInspectorId: otherId);

        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var ids = await AllLeadIdsAsync(client, "/api/v1/leads?pageSize=100");

        Assert.Contains(mine, ids);
        Assert.Contains(theirs, ids);
    }

    [Fact]
    public async Task Inspector_sees_only_their_own_leads()
    {
        var inspectorId = await InspectorIdAsync();
        var otherId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var mine = await SeedLeadAsync(assignedInspectorId: inspectorId);
        var theirs = await SeedLeadAsync(assignedInspectorId: otherId);

        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var ids = await AllLeadIdsAsync(client, "/api/v1/leads?pageSize=100");

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);
    }

    [Fact]
    public async Task Inspector_cannot_widen_their_scope_by_supplying_another_inspectors_id()
    {
        var inspectorId = await InspectorIdAsync();
        var otherId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var mine = await SeedLeadAsync(assignedInspectorId: inspectorId);
        var theirs = await SeedLeadAsync(assignedInspectorId: otherId);

        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        // Asking for someone else's Leads must return the caller's own, not theirs and not an error
        // — the filter is overridden server-side, per PermissionMatrix §1's "filtered server-side".
        var ids = await AllLeadIdsAsync(client, $"/api/v1/leads?assignedInspectorId={otherId}&pageSize=100");

        Assert.DoesNotContain(theirs, ids);
        Assert.Contains(mine, ids);
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var newLead = await SeedLeadAsync(assignedInspectorId: null);
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var wonIds = await AllLeadIdsAsync(client, $"/api/v1/leads?status={nameof(LeadStatus.Won)}&pageSize=100");
        var newIds = await AllLeadIdsAsync(client, $"/api/v1/leads?status={nameof(LeadStatus.New)}&pageSize=100");

        Assert.DoesNotContain(newLead, wonIds);
        Assert.Contains(newLead, newIds);
    }

    [Fact]
    public async Task Returns_paging_metadata_and_respects_page_size()
    {
        await SeedLeadAsync(assignedInspectorId: null);
        await SeedLeadAsync(assignedInspectorId: null);
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var body = await (await client.GetAsync("/api/v1/leads?page=1&pageSize=1")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
        Assert.Single(body.GetProperty("items").EnumerateArray());

        // TotalCount describes the whole filtered set, not the page — otherwise a client could
        // never render page controls.
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?createdFrom=2026-12-31&createdTo=2026-01-01")]
    public async Task Rejects_invalid_paging_and_ranges(string queryString)
    {
        using var client = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.GetAsync($"/api/v1/leads{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Slice 5's deferred Location header ----------

    [Fact]
    public async Task Creating_a_lead_now_returns_a_location_header_pointing_at_the_new_resource()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Location Header",
            phone = "+49 151 55555555",
            email = "location@example.de",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.EndsWith($"/{id}", location.ToString());

        // The header must point somewhere real — the whole reason it was deferred out of Slice 5.
        using var admin = await AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(location)).StatusCode);
    }

    // ---------- helpers ----------

    private Task<int> InspectorIdAsync() => factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

    private async Task<HttpClient> AuthenticatedClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Seeds directly through the Domain factory rather than the public endpoint, because that
    /// endpoint can only ever produce unassigned, website-sourced Leads — scoping tests need Leads
    /// belonging to a specific Inspector.
    /// </summary>
    private async Task<int> SeedLeadAsync(int? assignedInspectorId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create($"Seeded {Guid.NewGuid():N}", "+49 151 99999999", "seeded@example.de", LeadSource.Phone);

        if (assignedInspectorId is { } inspectorId)
        {
            lead.AssignInspector(inspectorId);
        }

        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        return lead.Id;
    }

    private static async Task<IReadOnlyList<int>> AllLeadIdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
    }
}
