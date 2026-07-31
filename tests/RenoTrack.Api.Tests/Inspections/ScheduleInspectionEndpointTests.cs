using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Inspections;

/// <summary>
/// <c>POST /api/v1/leads/{leadId}/inspections</c> (SRS FR-2.3). Admin only per
/// <c>PermissionMatrix.md</c> §2. The assertions that matter most are the ones no unit test can
/// make: that the role gate really rejects an Inspector, and that BR-13's side effect on the Lead
/// actually reaches the database.
/// </summary>
[Collection("Api")]
public sealed class ScheduleInspectionEndpointTests(RenoTrackApiFactory factory)
{
    private static readonly DateTime ScheduledAt = new(2026, 9, 10, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Admin_can_schedule_an_inspection()
    {
        var leadId = await SeedNewLeadAsync();
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);
        using var client = await AdminClientAsync();

        var response = await ScheduleAsync(client, leadId, inspectorId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("id").GetInt32() > 0);
        Assert.Equal(leadId, body.GetProperty("leadId").GetInt32());
        Assert.Equal(inspectorId, body.GetProperty("inspectorId").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task Scheduling_assigns_the_inspector_to_the_lead_and_advances_its_status()
    {
        var leadId = await SeedNewLeadAsync();
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);
        using var client = await AdminClientAsync();

        await ScheduleAsync(client, leadId, inspectorId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        // BR-13 — scheduling assigns the Inspector to the Lead, not just to the Inspection. This is
        // the endpoint's real side effect and the reason it is more than a create.
        Assert.Equal(inspectorId, lead.AssignedInspectorId);
        Assert.Equal(LeadStatus.InspectionScheduled, lead.Status);
    }

    [Fact]
    public async Task An_inspector_may_not_schedule()
    {
        var leadId = await SeedNewLeadAsync();
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);
        using var client = await ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await ScheduleAsync(client, leadId, inspectorId);

        // PermissionMatrix §2 grants Inspector nothing for this action.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var leadId = await SeedNewLeadAsync();
        using var client = factory.CreateClient();

        var response = await ScheduleAsync(client, leadId, inspectorId: 1);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scheduling_against_an_unknown_lead_returns_404()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);
        using var client = await AdminClientAsync();

        var response = await ScheduleAsync(client, leadId: 999999, inspectorId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scheduling_a_lead_that_is_already_scheduled_returns_409()
    {
        var leadId = await SeedNewLeadAsync();
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);
        using var client = await AdminClientAsync();

        await ScheduleAsync(client, leadId, inspectorId);
        var second = await ScheduleAsync(client, leadId, inspectorId);

        // Lead.MarkInspectionScheduled()'s own guard (status must be New) rejects this — the
        // controller checks no status itself, and D59 maps InvalidOperationException to 409.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---- assignee eligibility ----

    [Fact]
    public async Task Scheduling_to_a_non_existent_user_returns_404_rather_than_a_database_error()
    {
        var leadId = await SeedNewLeadAsync();
        using var client = await AdminClientAsync();

        var response = await ScheduleAsync(client, leadId, inspectorId: 999999);

        // Without the eligibility check this reached SaveChangesAsync and failed on the AspNetUsers
        // foreign key, surfacing as an unmapped 500 for an ordinary mistyped id.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scheduling_to_an_admin_is_rejected()
    {
        var leadId = await SeedNewLeadAsync();
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var client = await AdminClientAsync();

        var response = await ScheduleAsync(client, leadId, inspectorId: adminId);

        // A real user with a valid FK, so the database would have accepted it — only the business
        // rule rejects assigning site work to someone who is not an Inspector.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scheduling_to_a_deactivated_inspector_is_rejected()
    {
        var leadId = await SeedNewLeadAsync();
        var inactiveId = await factory.GetUserIdAsync(RenoTrackApiFactory.InactiveEmail);
        using var client = await AdminClientAsync();

        var response = await ScheduleAsync(client, leadId, inspectorId: inactiveId);

        // The seeded inactive user holds the Inspector role, so only the IsActive check rejects it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_assignee_leaves_the_lead_untouched()
    {
        var leadId = await SeedNewLeadAsync();
        using var client = await AdminClientAsync();

        await ScheduleAsync(client, leadId, inspectorId: 999999);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        Assert.Null(lead.AssignedInspectorId);
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public async Task An_invalid_body_is_rejected_with_400()
    {
        var leadId = await SeedNewLeadAsync();
        using var client = await AdminClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspections",
            new { scheduledAt = ScheduledAt, inspectorId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers ----

    private static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, int leadId, int inspectorId) =>
        client.PostAsJsonAsync(
            $"/api/v1/leads/{leadId}/inspections",
            new { scheduledAt = ScheduledAt, inspectorId });

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

    private async Task<int> SeedNewLeadAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create($"Schedule {Guid.NewGuid():N}", "+49 151 77777777", "schedule@example.de", LeadSource.Phone);
        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        return lead.Id;
    }
}
