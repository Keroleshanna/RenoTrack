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
/// The two Lead write endpoints added in Phase 10 (<c>PermissionMatrix.md</c> §1): correcting
/// contact details, and assigning the responsible Inspector.
/// </summary>
/// <remarks>
/// The authorization assertions carry the weight here. Contact editing is the split case — Admin
/// <c>F</c>, Inspector <c>S</c> — so it must prove an Inspector reaches their own Lead and is
/// refused another's. Assignment is Admin <c>F</c> only, so it must prove an Inspector is refused
/// outright regardless of whose Lead it is.
/// </remarks>
[Collection("Api")]
public sealed class LeadWriteEndpointsTests(RenoTrackApiFactory factory)
{
    // ---------- PUT /leads/{id} ----------

    [Fact]
    public async Task Admin_can_correct_any_leads_contact_details()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: await InspectorIdAsync());
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "Korrigierter Name",
            phone = "+49 151 60000001",
            email = "korrigiert@example.de",
            address = "Neue Straße 5, 40213 Düsseldorf",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Korrigierter Name", body.GetProperty("name").GetString());
        Assert.Equal("korrigiert@example.de", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Inspector_can_correct_their_own_lead()
    {
        var inspectorId = await InspectorIdAsync();
        var leadId = await SeedLeadAsync(assignedInspectorId: inspectorId);
        using var client = await InspectorAsync();

        // §1's own example: an Inspector fixing a wrong phone number found on-site.
        var response = await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "Vor Ort Korrektur",
            phone = "+49 151 60000002",
            email = "vorort@example.de",
            address = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_from_correcting_another_inspectors_lead()
    {
        var otherInspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var leadId = await SeedLeadAsync(assignedInspectorId: otherInspectorId);
        using var client = await InspectorAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "Fremder Lead",
            phone = "+49 151 60000003",
            email = "fremd@example.de",
            address = (string?)null,
        });

        // 403, not 404 — the row exists and the caller simply may not touch it (CLAUDE.md §16).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Correcting_replaces_the_address_and_persists()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null, address: "Alte Straße 9");
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "Adresse Geleert",
            phone = "+49 151 60000004",
            email = "geleert@example.de",
            address = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        // PUT replaces all four fields, so an omitted address clears it rather than being ignored.
        Assert.Null(lead.Address);
        Assert.Equal("Adresse Geleert", lead.Name);
    }

    [Fact]
    public async Task Correcting_does_not_change_status_or_assignment()
    {
        var inspectorId = await InspectorIdAsync();
        var leadId = await SeedLeadAsync(assignedInspectorId: inspectorId);
        using var client = await AdminAsync();

        await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "Unverändert",
            phone = "+49 151 60000005",
            email = "unveraendert@example.de",
            address = (string?)null,
        });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        // BR-7: status moves only through named transitions, never as a side effect of an edit.
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Equal(inspectorId, lead.AssignedInspectorId);
    }

    [Fact]
    public async Task Correcting_an_unknown_lead_is_404()
    {
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync("/api/v1/leads/999999", new
        {
            name = "Nicht Vorhanden",
            phone = "+49 151 60000006",
            email = "nichtda@example.de",
            address = (string?)null,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Correcting_with_a_blank_name_is_a_field_keyed_400()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/leads/{leadId}", new
        {
            name = "",
            phone = "+49 151 60000007",
            email = "leer@example.de",
            address = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Name", out _));
    }

    // ---------- PUT /leads/{id}/inspector ----------

    [Fact]
    public async Task Admin_can_assign_an_inspector_to_an_unassigned_lead()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        var inspectorId = await InspectorIdAsync();
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspector", new { inspectorId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(inspectorId, body.GetProperty("assignedInspectorId").GetInt32());
    }

    [Fact]
    public async Task Admin_can_reassign_a_lead_to_a_different_inspector()
    {
        var first = await InspectorIdAsync();
        var second = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var leadId = await SeedLeadAsync(assignedInspectorId: first);
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspector", new { inspectorId = second });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        Assert.Equal(second, lead.AssignedInspectorId);

        // Reassignment is administrative — StateMachine.md defines no transition for it.
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public async Task Assigning_an_inactive_account_is_404()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        var inactiveId = await factory.GetUserIdAsync(RenoTrackApiFactory.InactiveEmail);
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspector", new { inspectorId = inactiveId });

        // A real row with a real Inspector role, so an FK would have accepted it — D62's point.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_an_admin_account_is_404()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspector", new { inspectorId = adminId });

        // "Right person, wrong role" is the case a foreign key can never catch.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_from_assigning_even_their_own_lead()
    {
        var inspectorId = await InspectorIdAsync();
        var leadId = await SeedLeadAsync(assignedInspectorId: inspectorId);
        using var client = await InspectorAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspector", new { inspectorId });

        // §1 marks this row Admin F, Inspector — . Owning the Lead grants no say in who owns it.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_records_the_target_inspector_in_the_audit_trail()
    {
        var leadId = await SeedLeadAsync(assignedInspectorId: null);
        var inspectorId = await InspectorIdAsync();
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var client = await AdminAsync();

        await client.PutAsJsonAsync($"/api/v1/leads/{leadId}/inspector", new { inspectorId });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var entry = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleAsync(a => a.EntityType == "Lead"
                && a.EntityId == leadId
                && a.Action == RenoTrack.Application.Common.AuditAction.LeadInspectorAssigned);

        Assert.Equal(adminId, entry.PerformedByUserId);

        // Without the target id the entry records that something happened but not the one fact
        // that makes it worth reading.
        Assert.Equal(inspectorId.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Details);
    }

    // ---------- helpers ----------

    private Task<int> InspectorIdAsync() => factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

    private Task<HttpClient> AdminAsync() =>
        AuthenticatedClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

    private Task<HttpClient> InspectorAsync() =>
        AuthenticatedClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

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
    /// Seeds through the Domain factory rather than an endpoint, because the public form can only
    /// produce unassigned website Leads and these tests need a specific Inspector's Lead.
    /// </summary>
    private async Task<int> SeedLeadAsync(int? assignedInspectorId, string? address = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create(
            "Seed Lead", "+49 151 00000000", "seed@example.de", LeadSource.Phone, address);

        if (assignedInspectorId is { } inspectorId)
        {
            lead.AssignInspector(inspectorId);
        }

        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        return lead.Id;
    }
}
