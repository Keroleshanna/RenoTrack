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
/// <c>POST /api/v1/inspections/{id}/complete</c> (SRS FR-3.4, Architecture.md §5.2). Inspector only,
/// and specifically the assigned one, per <c>PermissionMatrix.md</c> §2's "— | S".
/// </summary>
/// <remarks>
/// <para>
/// The assertions that carry this slice are the ones made against the database rather than the
/// response. <c>InspectionDto</c> has no Lead field, so completion's cross-aggregate side effect —
/// the Lead moving to <c>InspectionDone</c> — is <b>invisible over HTTP</b>. A test that only checked
/// the 200 and the returned <c>completedAt</c> would still pass if <c>lead.MarkInspectionDone()</c>
/// were deleted from the handler outright.
/// </para>
/// <para>
/// The mirror of that is <see cref="A_lead_in_the_wrong_state_is_rejected_and_the_inspection_is_not_completed_in_the_database"/>:
/// the handler mutates the Inspection in memory <em>before</em> the Lead's guard runs, so only a
/// database read can prove that mutation was discarded when the request scope ended. The fakes used
/// by the Application-layer tests have no change tracker and cannot prove it.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed class CompleteInspectionEndpointTests(RenoTrackApiFactory factory)
{
    private static readonly DateTime ScheduledAt = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task Assigned_inspector_can_complete_the_inspection()
    {
        var (_, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(inspectionId, body.GetProperty("id").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("completedAt").ValueKind);
    }

    /// <summary>
    /// The test the slice exists for: both aggregates are read back from the database, because the
    /// response cannot show the Lead moved. Deleting <c>lead.MarkInspectionDone()</c> from the handler
    /// fails here and nowhere else.
    /// </summary>
    [Fact]
    public async Task Completion_persists_both_the_inspection_timestamp_and_the_lead_status()
    {
        var (leadId, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var (lead, inspection) = await ReadBackAsync(leadId, inspectionId);

        Assert.NotNull(inspection.CompletedAt);
        Assert.Equal(LeadStatus.InspectionDone, lead.Status);
    }

    /// <summary>
    /// Pins that nothing about the Inspection's content gates completion. No document defines a photo
    /// or notes precondition (SRS FR-3.2 grants a capability, FR-3.4 states only the consequence,
    /// StateMachine.md §1.3's guard is "Inspection belongs to this Lead", and no BusinessRules.md entry
    /// mentions either), so a future edit that invents one must fail a test rather than pass review.
    /// </summary>
    [Fact]
    public async Task An_inspection_with_no_photos_and_no_notes_can_still_be_completed()
    {
        var (_, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var (_, inspection) = await ReadBackAsync(leadId: null, inspectionId);
        Assert.Empty(inspection.Photos);
        Assert.Null(inspection.Notes);
    }

    // ---- Authorization ----------------------------------------------------

    /// <summary>
    /// <c>PermissionMatrix.md</c> §2 grants Admin nothing here, inverting Slice 7's scheduling
    /// endpoint — completion is recorded by whoever was actually on site.
    /// </summary>
    /// <remarks>
    /// The empty-body assertion is load-bearing and was added after an adversarial experiment showed a
    /// status-code check alone proves nothing here. Two independent layers can produce this 403: the
    /// action's <c>[Authorize(Roles = Roles.Inspector)]</c>, and <c>EnsureInspectionOwnership</c> inside
    /// the handler (an Admin is never the assigned Inspector). Weakening the role attribute to a bare
    /// <c>[Authorize]</c> therefore still yielded 403 — from ownership — and the test passed while the
    /// role gate was gone. The class-level attribute admits both roles, so it does not close the gap.
    ///
    /// A role-gate rejection is emitted by the authorization middleware with <b>no body</b>; a
    /// <c>ForbiddenException</c> reaching the D59 handler produces a ProblemDetails document naming the
    /// inspector and inspection. Asserting the body is empty is what pins <em>which</em> layer rejected,
    /// making this test actually detect the drift CLAUDE.md §22 warns about.
    /// </remarks>
    [Fact]
    public async Task An_admin_is_forbidden_by_the_role_gate_before_reaching_the_handler()
    {
        var (leadId, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = await ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        var (lead, inspection) = await ReadBackAsync(leadId, inspectionId);
        Assert.Null(inspection.CompletedAt);
        Assert.Equal(LeadStatus.InspectionScheduled, lead.Status);
    }

    /// <summary>The "S" in PermissionMatrix §2 — an Inspector may complete only their own Inspection.</summary>
    [Fact]
    public async Task A_non_owning_inspector_is_forbidden_and_neither_aggregate_changes()
    {
        var otherInspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var (leadId, inspectionId) = await SeedScheduledInspectionAsync(inspectorId: otherInspectorId);
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var (lead, inspection) = await ReadBackAsync(leadId, inspectionId);
        Assert.Null(inspection.CompletedAt);
        Assert.Equal(LeadStatus.InspectionScheduled, lead.Status);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var (_, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Guards -----------------------------------------------------------

    [Fact]
    public async Task An_unknown_inspection_is_not_found()
    {
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync("/api/v1/inspections/987654/complete", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Repeated completion is deliberately not idempotent (the same stance D61 took for
    /// <c>POST /api/v1/leads</c>). The Inspection's own guard produces the 409 — which is why the
    /// handler's ordering puts it first — and the original timestamp must survive untouched, since
    /// overwriting it would undo exactly the evidentiary value BR-10 protects.
    /// </summary>
    [Fact]
    public async Task A_second_completion_is_a_conflict_and_does_not_overwrite_the_original_timestamp()
    {
        var (_, inspectionId) = await SeedScheduledInspectionAsync();
        using var client = await InspectorClientAsync();

        var first = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var (_, afterFirst) = await ReadBackAsync(leadId: null, inspectionId);
        var originalCompletedAt = afterFirst.CompletedAt;

        var second = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var (_, afterSecond) = await ReadBackAsync(leadId: null, inspectionId);
        Assert.Equal(originalCompletedAt, afterSecond.CompletedAt);
    }

    /// <summary>
    /// The cross-aggregate rollback proof. The Lead is seeded at <c>New</c>, so <c>Complete()</c>
    /// succeeds in memory and <c>MarkInspectionDone()</c> then throws — meaning the Inspection really
    /// is dirty in the change tracker at the moment the request fails. Only this database read can show
    /// that the mutation never reached storage. Moving <c>SaveChangesAsync</c> above the Lead mutation
    /// makes this test fail with a non-null <c>CompletedAt</c>.
    ///
    /// The state is seeded directly because it is currently unreachable through the API: a Lead can only
    /// receive an Inspection while it is <c>New</c>, and scheduling immediately advances it to
    /// <c>InspectionScheduled</c>. It becomes reachable through data import, PermissionMatrix §2's
    /// documented-but-unbuilt Inspection reassignment, or BR-10's anticipated "reopen" use case.
    /// </summary>
    [Fact]
    public async Task A_lead_in_the_wrong_state_is_rejected_and_the_inspection_is_not_completed_in_the_database()
    {
        var (leadId, inspectionId) = await SeedScheduledInspectionAsync(advanceLeadToScheduled: false);
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var (lead, inspection) = await ReadBackAsync(leadId, inspectionId);
        Assert.Null(inspection.CompletedAt);
        Assert.Equal(LeadStatus.New, lead.Status);
    }

    [Fact]
    public async Task A_non_positive_id_is_rejected_as_a_bad_request()
    {
        using var client = await InspectorClientAsync();

        var response = await client.PostAsync("/api/v1/inspections/0/complete", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Helpers ----------------------------------------------------------

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

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
    /// Seeds a Lead plus its Inspection directly. Scheduling through the API would need an Admin client
    /// and would make the deliberately-inconsistent case (<paramref name="advanceLeadToScheduled"/>
    /// false) impossible to construct at all.
    /// </summary>
    private async Task<(int LeadId, int InspectionId)> SeedScheduledInspectionAsync(
        int? inspectorId = null,
        bool advanceLeadToScheduled = true)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var assignedTo = inspectorId ?? await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        var lead = Lead.Create($"Complete {Guid.NewGuid():N}", "+49 151 77777777", "complete@example.de", LeadSource.Phone);

        if (advanceLeadToScheduled)
        {
            lead.MarkInspectionScheduled();
        }

        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        var inspection = Inspection.Schedule(lead.Id, ScheduledAt, assignedTo);
        dbContext.Inspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        return (lead.Id, inspection.Id);
    }

    /// <summary>
    /// Reads both aggregates back with <c>AsNoTracking</c> on a fresh scope, so what is asserted is what
    /// the database holds rather than anything left in a change tracker.
    /// </summary>
    private async Task<(Lead Lead, Inspection Inspection)> ReadBackAsync(int? leadId, int inspectionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var inspection = await dbContext.Inspections
            .AsNoTracking()
            .Include(i => i.Photos)
            .SingleAsync(i => i.Id == inspectionId);

        var lead = await dbContext.Leads
            .AsNoTracking()
            .SingleAsync(l => l.Id == (leadId ?? inspection.LeadId));

        return (lead, inspection);
    }
}
