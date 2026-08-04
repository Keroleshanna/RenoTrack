using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves <c>AngebotQueries.GetForLeadAsync</c> against real LocalDB: that the DTO projection is
/// genuinely translatable by EF Core (a real risk, not an assumption — <c>NetTotal</c> goes through
/// a value converter), that the Inspector scope is applied in SQL rather than after loading, and
/// that the ordering is deterministic.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class AngebotQueriesTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>
    /// A unique Angebot number per row. <c>AngebotNumber</c> carries a unique index, and this class
    /// shares one database with every other test in the collection, so a fixed prefix plus a counter
    /// would eventually collide with a rerun.
    /// </summary>
    private static string NextNumber() => $"ANG-Q-{Guid.NewGuid():N}"[..18];

    /// <summary>
    /// <c>Angebot.CreatedByInspectorId</c> is a real FK to <c>AspNetUsers</c> as of Slice 15
    /// (D44), so an invented id is rejected by the database — inspector ids must be persisted
    /// users. Same helper as <see cref="AngebotRepositoryTests"/>.
    /// </summary>
    private async Task<int> SeedApplicationUserAsync(string name)
    {
        var user = new ApplicationUser { Name = name };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Persists an Angebot with one section and one item so the stored totals are non-zero, and
    /// returns the Lead it belongs to. Each test gets its own Lead, so the shared database's other
    /// rows can never affect an assertion.
    /// </summary>
    private async Task<(int LeadId, Angebot Angebot)> SeedAngebotAsync(
        string number,
        int createdByInspectorId,
        int? existingLeadId = null,
        decimal unitPrice = 100.00m)
    {
        await using var context = fixture.CreateContext();

        int leadId;

        if (existingLeadId is { } id)
        {
            leadId = id;
        }
        else
        {
            var lead = Lead.Create("Angebot query lead", "0176 0000001", "angebot-query@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();
            leadId = lead.Id;
        }

        var angebot = Angebot.Create(leadId, inspectionId: null, number, createdByInspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(unitPrice), VatRate.Standard);

        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        return (leadId, angebot);
    }

    [Fact]
    public async Task GetForLeadAsync_ProjectsEveryFieldCorrectly()
    {
        var inspectorId = await SeedApplicationUserAsync("Owning Inspector");
        var (leadId, angebot) = await SeedAngebotAsync(NextNumber(), inspectorId);

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotQueries(readContext);

        var results = await queries.GetForLeadAsync(leadId, requestingInspectorId: null, CancellationToken.None);

        var dto = Assert.Single(results);
        Assert.Equal(angebot.Id, dto.Id);
        Assert.Equal(leadId, dto.LeadId);
        Assert.Null(dto.InspectionId);
        Assert.Equal(angebot.AngebotNumber, dto.AngebotNumber);
        Assert.Equal(AngebotStatus.Draft, dto.Status);
        Assert.Equal(inspectorId, dto.CreatedByInspectorId);
        Assert.Null(dto.ReviewedByAdminId);

        // The stored, converted Money columns — the projection reads them rather than re-deriving.
        Assert.Equal(100.00m, dto.NetTotal);
        Assert.Equal(119.00m, dto.GrossTotal);
    }

    [Fact]
    public async Task GetForLeadAsync_ReturnsOnlyThatLeadsAngebote()
    {
        var inspectorId = await SeedApplicationUserAsync("Owning Inspector");
        var (leadId, _) = await SeedAngebotAsync(NextNumber(), inspectorId);
        await SeedAngebotAsync(NextNumber(), inspectorId);

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotQueries(readContext);

        var results = await queries.GetForLeadAsync(leadId, null, CancellationToken.None);

        Assert.All(results, dto => Assert.Equal(leadId, dto.LeadId));
        Assert.Single(results);
    }

    /// <summary>
    /// The scope that matters: an Inspector sees only what they created, and an Admin (null) sees
    /// both. Applied as a <c>WHERE</c> clause, which is the only way a collection read can be
    /// scoped without loading everything first (CLAUDE.md §22).
    /// </summary>
    [Fact]
    public async Task GetForLeadAsync_ScopesToTheRequestingInspector()
    {
        var owningInspectorId = await SeedApplicationUserAsync("Owning Inspector");
        var otherInspectorId = await SeedApplicationUserAsync("Other Inspector");

        var (leadId, mine) = await SeedAngebotAsync(NextNumber(), owningInspectorId);
        var (_, theirs) = await SeedAngebotAsync(NextNumber(), otherInspectorId, existingLeadId: leadId);

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotQueries(readContext);

        var scoped = await queries.GetForLeadAsync(leadId, owningInspectorId, CancellationToken.None);
        var unscoped = await queries.GetForLeadAsync(leadId, null, CancellationToken.None);

        Assert.Equal([mine.Id], scoped.Select(d => d.Id));
        Assert.Equal(2, unscoped.Count);
        Assert.Contains(unscoped, d => d.Id == theirs.Id);
    }

    /// <summary>
    /// Newest first. Both rows are created within the same test, so <c>CreatedAt</c> can legitimately
    /// tie — which is exactly why the query carries an <c>Id</c> tiebreaker, and why asserting on a
    /// stable order here is meaningful rather than incidental.
    /// </summary>
    [Fact]
    public async Task GetForLeadAsync_OrdersNewestFirstWithADeterministicTiebreaker()
    {
        var inspectorId = await SeedApplicationUserAsync("Owning Inspector");
        var (leadId, first) = await SeedAngebotAsync(NextNumber(), inspectorId);
        var (_, second) = await SeedAngebotAsync(NextNumber(), inspectorId, existingLeadId: leadId);

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotQueries(readContext);

        var results = await queries.GetForLeadAsync(leadId, null, CancellationToken.None);

        Assert.Equal([second.Id, first.Id], results.Select(d => d.Id));
    }

    [Fact]
    public async Task GetForLeadAsync_ReturnsEmptyForALeadWithNoAngebote()
    {
        await using var context = fixture.CreateContext();
        var lead = Lead.Create("No angebote", "0176 0000002", "no-angebote@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var queries = new AngebotQueries(context);

        Assert.Empty(await queries.GetForLeadAsync(lead.Id, null, CancellationToken.None));
    }
}
