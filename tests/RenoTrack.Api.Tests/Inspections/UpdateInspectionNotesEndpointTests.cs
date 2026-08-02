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
/// <c>PATCH /api/v1/inspections/{id}</c> (SRS FR-3.3, Sequence Diagram §3 Step B). Inspector only,
/// and specifically the assigned one, per <c>PermissionMatrix.md</c> §2's "— | S".
/// </summary>
/// <remarks>
/// Every assertion about what actually changed is made against the database rather than the returned
/// <c>InspectionDto</c>. The DTO is produced from the same in-memory aggregate the handler mutated, so
/// it proves the mutation happened but not that it was ever committed — a distinction Slice 9's
/// adversarial experiments demonstrated is real rather than theoretical.
/// </remarks>
[Collection("Api")]
public sealed class UpdateInspectionNotesEndpointTests(RenoTrackApiFactory factory)
{
    private static readonly DateTime ScheduledAt = new(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
    private const string Notes = "Re-tile bathroom floor and walls, ~10m2";

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task Assigned_inspector_can_update_the_notes()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId, Notes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(inspectionId, body.GetProperty("id").GetInt32());
        Assert.Equal(Notes, body.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task The_notes_are_persisted_to_the_database()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId, Notes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var inspection = await ReadBackAsync(inspectionId);
        Assert.Equal(Notes, inspection.Notes);
    }

    /// <summary>
    /// Clearing is a supported operation, not an edge case: <c>Inspection.UpdateNotes</c> accepts null
    /// and the validator places no rule on the field. Asserted against the database, since a DTO
    /// showing null would also be consistent with the update never committing.
    /// </summary>
    [Fact]
    public async Task Sending_null_clears_existing_notes()
    {
        var inspectionId = await SeedInspectionAsync(notes: "first draft");
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId, notes: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadBackAsync(inspectionId)).Notes);
    }

    /// <summary>Pins that this endpoint is idempotent — unlike completion, a repeat is legitimate, not a 409.</summary>
    [Fact]
    public async Task Repeating_the_same_update_is_allowed()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(client, inspectionId, Notes)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(client, inspectionId, Notes)).StatusCode);

        Assert.Equal(Notes, (await ReadBackAsync(inspectionId)).Notes);
    }

    // ---- Authorization ----------------------------------------------------

    /// <summary>
    /// The empty-body assertion is load-bearing, carried over from the Slice 9 finding: two independent
    /// layers can produce this 403 — the action's <c>[Authorize(Roles = Roles.Inspector)]</c> and
    /// <c>EnsureInspectionOwnership</c> in the handler (an Admin is never the assigned Inspector). A
    /// status-code-only check would therefore pass even with the role attribute removed. A role-gate
    /// rejection carries <b>no body</b>; a <c>ForbiddenException</c> reaching the D59 handler produces a
    /// ProblemDetails document. Asserting emptiness is what pins <em>which</em> layer rejected.
    /// </summary>
    [Fact]
    public async Task An_admin_is_forbidden_by_the_role_gate_before_reaching_the_handler()
    {
        var inspectionId = await SeedInspectionAsync(notes: "untouched");
        using var client = await ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await PatchAsync(client, inspectionId, "admin tried to edit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Equal("untouched", (await ReadBackAsync(inspectionId)).Notes);
    }

    [Fact]
    public async Task A_non_owning_inspector_is_forbidden_and_the_notes_are_unchanged()
    {
        var otherInspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var inspectionId = await SeedInspectionAsync(inspectorId: otherInspectorId, notes: "untouched");
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId, "not my inspection");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("untouched", (await ReadBackAsync(inspectionId)).Notes);
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = factory.CreateClient();

        var response = await PatchAsync(client, inspectionId, Notes);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Guards -----------------------------------------------------------

    /// <summary>BR-10 — a completed Inspection is immutable, enforced by the aggregate's own guard.</summary>
    [Fact]
    public async Task A_completed_inspection_cannot_be_edited_and_the_notes_are_unchanged()
    {
        var inspectionId = await SeedInspectionAsync(notes: "recorded on site", completed: true);
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId, "sneaking in a later change");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recorded on site", (await ReadBackAsync(inspectionId)).Notes);
    }

    [Fact]
    public async Task An_unknown_inspection_is_not_found()
    {
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId: 987654, Notes);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_non_positive_id_is_rejected_as_a_bad_request()
    {
        using var client = await InspectorClientAsync();

        var response = await PatchAsync(client, inspectionId: 0, Notes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Helpers ----------------------------------------------------------

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, int inspectionId, string? notes) =>
        client.PatchAsJsonAsync($"/api/v1/inspections/{inspectionId}", new { notes });

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
    /// Seeds directly, because no endpoint can produce a completed Inspection and scheduling through the
    /// API would need an Admin plus a Lead in exactly the right state.
    /// </summary>
    private async Task<int> SeedInspectionAsync(int? inspectorId = null, string? notes = null, bool completed = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var assignedTo = inspectorId ?? await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        var lead = Lead.Create($"Notes {Guid.NewGuid():N}", "+49 151 66666666", "notes@example.de", LeadSource.Phone);
        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        var inspection = Inspection.Schedule(lead.Id, ScheduledAt, assignedTo);

        if (notes is not null)
        {
            inspection.UpdateNotes(notes);
        }

        if (completed)
        {
            inspection.Complete();
        }

        dbContext.Inspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        return inspection.Id;
    }

    private async Task<Inspection> ReadBackAsync(int inspectionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        return await dbContext.Inspections
            .AsNoTracking()
            .SingleAsync(i => i.Id == inspectionId);
    }
}
