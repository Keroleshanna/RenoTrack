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
/// <c>PUT /api/v1/inspections/{id}/inspector</c> (<c>PermissionMatrix.md</c> §2 "Reassign an
/// Inspection to a different Inspector — Admin F").
/// </summary>
/// <remarks>
/// The two assertions that matter most here are the ones only a real request can make: that BR-13
/// follows the visit — the Lead's assigned Inspector moves too, in the same commit — and that
/// BR-10 stops the reassignment once the visit is finished.
/// </remarks>
[Collection("Api")]
public sealed class ReassignInspectionEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Admin_can_reassign_a_scheduled_inspection()
    {
        var (inspectionId, _) = await SeedScheduledInspectionAsync();
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(target, body.GetProperty("inspectorId").GetInt32());
    }

    [Fact]
    public async Task Reassignment_moves_the_leads_assigned_inspector_too()
    {
        var (inspectionId, leadId) = await SeedScheduledInspectionAsync();
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);
        var inspection = await dbContext.Inspections.AsNoTracking().SingleAsync(i => i.Id == inspectionId);

        // BR-13 re-applied. Without this the Lead would stay in the outgoing Inspector's
        // server-side-filtered pipeline while the incoming one could not see it at all — a scoping
        // bug, not merely stale data.
        Assert.Equal(target, lead.AssignedInspectorId);
        Assert.Equal(target, inspection.InspectorId);
    }

    [Fact]
    public async Task Reassignment_does_not_move_the_lead_status()
    {
        var (inspectionId, leadId) = await SeedScheduledInspectionAsync();
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        // Who is going changed; where the Lead sits in the pipeline did not (BR-7).
        Assert.Equal(LeadStatus.InspectionScheduled, lead.Status);
    }

    [Fact]
    public async Task Reassigning_a_completed_inspection_is_409()
    {
        var (inspectionId, _) = await SeedScheduledInspectionAsync(completed: true);
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        // BR-10: a finished visit is immutable evidence of who attended it.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reassigning_to_an_inactive_account_is_404()
    {
        var (inspectionId, _) = await SeedScheduledInspectionAsync();
        var inactiveId = await factory.GetUserIdAsync(RenoTrackApiFactory.InactiveEmail);
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = inactiveId });

        // A real row holding the Inspector role, so the FK would have allowed it (D62).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reassigning_an_unknown_inspection_is_404()
    {
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        var response = await client.PutAsJsonAsync(
            "/api/v1/inspections/999999/inspector", new { inspectorId = target });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_even_for_their_own_inspection()
    {
        var (inspectionId, _) = await SeedScheduledInspectionAsync();
        var target = await SecondInspectorIdAsync();
        using var client = await InspectorAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        // §2 marks this row Admin F, Inspector — . An Inspector cannot hand their own work away.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_reassignment_changes_nothing()
    {
        var (inspectionId, leadId) = await SeedScheduledInspectionAsync(completed: true);
        var originalInspector = await InspectorIdAsync();
        var target = await SecondInspectorIdAsync();
        using var client = await AdminAsync();

        await client.PutAsJsonAsync(
            $"/api/v1/inspections/{inspectionId}/inspector", new { inspectorId = target });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var inspection = await dbContext.Inspections.AsNoTracking().SingleAsync(i => i.Id == inspectionId);
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId);

        // The Domain guard fires before SaveChangesAsync, so neither aggregate half-moves.
        Assert.Equal(originalInspector, inspection.InspectorId);
        Assert.Equal(originalInspector, lead.AssignedInspectorId);
    }

    // ---------- helpers ----------

    private Task<int> InspectorIdAsync() => factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

    private Task<int> SecondInspectorIdAsync() =>
        factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);

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
    /// A Lead with a scheduled visit, in the state <c>ScheduleInspectionCommandHandler</c> would
    /// have left them — assigned Inspector set (BR-13) and the Lead moved to
    /// <c>InspectionScheduled</c> — seeded directly so these tests do not depend on that endpoint.
    /// </summary>
    private async Task<(int InspectionId, int LeadId)> SeedScheduledInspectionAsync(bool completed = false)
    {
        var inspectorId = await InspectorIdAsync();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create(
            "Reassign Lead", "+49 151 70000000", $"reassign-{Guid.NewGuid():N}@example.de", LeadSource.Phone);
        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        var inspection = Inspection.Schedule(lead.Id, DateTime.UtcNow.AddDays(3), inspectorId);

        lead.AssignInspector(inspectorId);
        lead.MarkInspectionScheduled();

        if (completed)
        {
            inspection.Complete();
            lead.MarkInspectionDone();
        }

        dbContext.Inspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        return (inspection.Id, lead.Id);
    }
}
