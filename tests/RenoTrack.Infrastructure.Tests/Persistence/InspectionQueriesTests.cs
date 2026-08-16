using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The Phase 10 Inspection reads, against real LocalDB.
/// </summary>
/// <remarks>
/// The scoping assertions here are **security tests, not convenience tests**: an Inspector must not
/// be able to read another Inspector's assignment, and the rule is a <c>WHERE</c> clause rather than
/// a guard that throws — so a regression would silently widen visibility rather than fail loudly.
/// Nothing but a test catches that.
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class InspectionQueriesTests(RenoTrackDbContextFixture fixture)
{
    private async Task<int> SeedInspectorAsync(string name)
    {
        var user = new ApplicationUser { Name = name };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(int InspectionId, int LeadId, string LeadName)> SeedInspectionAsync(
        int inspectorId,
        DateTime scheduledAt,
        bool complete = false)
    {
        await using var context = fixture.CreateContext();

        var leadName = $"Bauherr {Guid.NewGuid():N}"[..16];
        var lead = Lead.Create(leadName, "0221 555", "site@example.de", LeadSource.Website, "Musterweg 1, Köln");
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var inspection = Inspection.Schedule(lead.Id, scheduledAt, inspectorId);
        lead.MarkInspectionScheduled();

        if (complete)
        {
            inspection.Complete();
            lead.MarkInspectionDone();
        }

        context.Inspections.Add(inspection);
        await context.SaveChangesAsync();

        return (inspection.Id, lead.Id, leadName);
    }

    // ---- The single read -------------------------------------------------------------------------

    /// <summary>
    /// The address is the whole point of this DTO — it is read on a phone on the way to a building.
    /// </summary>
    [Fact]
    public async Task Returns_the_inspection_with_its_leads_contact_details()
    {
        var inspectorId = await SeedInspectorAsync("Detail Inspector");
        var (inspectionId, leadId, leadName) = await SeedInspectionAsync(
            inspectorId,
            DateTime.UtcNow.Date.AddDays(1).AddHours(9));

        await using var context = fixture.CreateContext();
        var result = await new InspectionQueries(context).GetByIdAsync(inspectionId, null, default);

        Assert.NotNull(result);
        Assert.Equal(leadId, result.LeadId);
        Assert.Equal(leadName, result.LeadName);
        Assert.Equal("Musterweg 1, Köln", result.LeadAddress);
        Assert.Equal("0221 555", result.LeadPhone);
        Assert.Equal(0, result.PhotoCount);
    }

    /// <summary>An Admin is "F" — unscoped — so null must mean "no restriction", not "no access".</summary>
    [Fact]
    public async Task Lets_an_admin_read_any_inspection()
    {
        var inspectorId = await SeedInspectorAsync("Someone Else");
        var (inspectionId, _, _) = await SeedInspectionAsync(inspectorId, DateTime.UtcNow.AddDays(2));

        await using var context = fixture.CreateContext();
        Assert.NotNull(await new InspectionQueries(context).GetByIdAsync(inspectionId, null, default));
    }

    [Fact]
    public async Task Lets_the_assigned_inspector_read_their_own()
    {
        var inspectorId = await SeedInspectorAsync("Owning Inspector");
        var (inspectionId, _, _) = await SeedInspectionAsync(inspectorId, DateTime.UtcNow.AddDays(2));

        await using var context = fixture.CreateContext();
        Assert.NotNull(
            await new InspectionQueries(context).GetByIdAsync(inspectionId, inspectorId, default));
    }

    /// <summary>
    /// The security assertion. A non-owning Inspector must find nothing — which the handler turns
    /// into 404, deliberately not 403, so the response cannot confirm the row exists.
    /// </summary>
    [Fact]
    public async Task Hides_another_inspectors_assignment_entirely()
    {
        var ownerId = await SeedInspectorAsync("Owner");
        var intruderId = await SeedInspectorAsync("Intruder");
        var (inspectionId, _, _) = await SeedInspectionAsync(ownerId, DateTime.UtcNow.AddDays(2));

        await using var context = fixture.CreateContext();
        Assert.Null(await new InspectionQueries(context).GetByIdAsync(inspectionId, intruderId, default));
    }

    [Fact]
    public async Task Returns_null_for_an_id_that_does_not_exist()
    {
        await using var context = fixture.CreateContext();
        Assert.Null(await new InspectionQueries(context).GetByIdAsync(987_654_321, null, default));
    }

    // ---- The schedule ----------------------------------------------------------------------------

    [Fact]
    public async Task Returns_only_visits_inside_the_window_earliest_first()
    {
        var inspectorId = await SeedInspectorAsync("Schedule Inspector");
        var day = DateTime.UtcNow.Date.AddDays(30);

        await SeedInspectionAsync(inspectorId, day.AddHours(14));
        await SeedInspectionAsync(inspectorId, day.AddHours(8));
        // Outside the window on both sides.
        await SeedInspectionAsync(inspectorId, day.AddDays(-1).AddHours(10));
        await SeedInspectionAsync(inspectorId, day.AddDays(1).AddHours(10));

        await using var context = fixture.CreateContext();
        var schedule = await new InspectionQueries(context)
            .GetScheduledAsync(day, day.AddDays(1), inspectorId, true, default);

        Assert.Equal([day.AddHours(8), day.AddHours(14)], schedule.Select(i => i.ScheduledAt));
    }

    /// <summary>The upper bound is exclusive, so `[day, day+1)` is exactly one day.</summary>
    [Fact]
    public async Task Treats_the_upper_bound_as_exclusive()
    {
        var inspectorId = await SeedInspectorAsync("Boundary Inspector");
        var day = DateTime.UtcNow.Date.AddDays(60);

        await SeedInspectionAsync(inspectorId, day.AddDays(1));

        await using var context = fixture.CreateContext();
        var schedule = await new InspectionQueries(context)
            .GetScheduledAsync(day, day.AddDays(1), inspectorId, true, default);

        Assert.Empty(schedule);
    }

    [Fact]
    public async Task Can_exclude_visits_already_completed()
    {
        var inspectorId = await SeedInspectorAsync("Completion Inspector");
        var day = DateTime.UtcNow.Date.AddDays(90);

        await SeedInspectionAsync(inspectorId, day.AddHours(9), complete: true);
        await SeedInspectionAsync(inspectorId, day.AddHours(11));

        await using var context = fixture.CreateContext();
        var queries = new InspectionQueries(context);

        var all = await queries.GetScheduledAsync(day, day.AddDays(1), inspectorId, true, default);
        var outstanding = await queries.GetScheduledAsync(day, day.AddDays(1), inspectorId, false, default);

        Assert.Equal(2, all.Count);
        Assert.Equal(day.AddHours(11), Assert.Single(outstanding).ScheduledAt);
    }

    /// <summary>The same scoping rule as the single read, applied to a collection.</summary>
    [Fact]
    public async Task Scopes_the_schedule_to_the_requesting_inspector()
    {
        var mine = await SeedInspectorAsync("Mine");
        var theirs = await SeedInspectorAsync("Theirs");
        var day = DateTime.UtcNow.Date.AddDays(120);

        await SeedInspectionAsync(mine, day.AddHours(9));
        await SeedInspectionAsync(theirs, day.AddHours(10));

        await using var context = fixture.CreateContext();
        var queries = new InspectionQueries(context);

        var scoped = await queries.GetScheduledAsync(day, day.AddDays(1), mine, true, default);
        var unscoped = await queries.GetScheduledAsync(day, day.AddDays(1), null, true, default);

        Assert.Equal(mine, Assert.Single(scoped).InspectorId);
        Assert.Equal(2, unscoped.Count);
    }
}
