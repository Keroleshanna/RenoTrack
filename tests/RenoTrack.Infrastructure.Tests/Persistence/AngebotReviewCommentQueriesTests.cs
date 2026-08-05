using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves <c>AngebotReviewCommentQueries.GetForAngebotAsync</c> against real LocalDB: the projection
/// is translatable, comments are filtered to their own Angebot, and the thread reads forwards.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class AngebotReviewCommentQueriesTests(RenoTrackDbContextFixture fixture)
{
    private static string NextNumber() => $"ANG-R-{Guid.NewGuid():N}"[..18];

    /// <summary>Both AngebotReviewComment.AdminUserId and Angebot.CreatedByInspectorId are real FKs (D44).</summary>
    private async Task<int> SeedApplicationUserAsync(string name)
    {
        var user = new ApplicationUser { Name = name };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedAngebotAsync(int inspectorId)
    {
        await using var context = fixture.CreateContext();

        var lead = Lead.Create("Review comment lead", "0176 0000003", "review@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var angebot = Angebot.Create(lead.Id, inspectionId: null, NextNumber(), inspectorId);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        return angebot.Id;
    }

    [Fact]
    public async Task GetForAngebotAsync_ProjectsEveryFieldCorrectly()
    {
        var inspectorId = await SeedApplicationUserAsync("Inspector");
        var adminId = await SeedApplicationUserAsync("Admin");
        var angebotId = await SeedAngebotAsync(inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.AngebotReviewComments.Add(
                AngebotReviewComment.Create(angebotId, adminId, "Bitte Position 2 neu kalkulieren."));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotReviewCommentQueries(readContext);

        var dto = Assert.Single(await queries.GetForAngebotAsync(angebotId, CancellationToken.None));
        Assert.Equal(angebotId, dto.AngebotId);
        Assert.Equal(adminId, dto.AdminUserId);
        Assert.Equal("Bitte Position 2 neu kalkulieren.", dto.Comment);
        Assert.NotEqual(default, dto.CreatedAt);
    }

    [Fact]
    public async Task GetForAngebotAsync_ReturnsOnlyThatAngebotsComments()
    {
        var inspectorId = await SeedApplicationUserAsync("Inspector");
        var adminId = await SeedApplicationUserAsync("Admin");
        var mine = await SeedAngebotAsync(inspectorId);
        var other = await SeedAngebotAsync(inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.AngebotReviewComments.Add(AngebotReviewComment.Create(mine, adminId, "Mine"));
            writeContext.AngebotReviewComments.Add(AngebotReviewComment.Create(other, adminId, "Theirs"));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotReviewCommentQueries(readContext);

        var result = await queries.GetForAngebotAsync(mine, CancellationToken.None);

        Assert.Equal("Mine", Assert.Single(result).Comment);
    }

    /// <summary>
    /// Oldest first. Both rows are written in the same test, so <c>CreatedAt</c> can tie — which is
    /// exactly why the query carries an <c>Id</c> tiebreaker.
    /// </summary>
    [Fact]
    public async Task GetForAngebotAsync_OrdersOldestFirst()
    {
        var inspectorId = await SeedApplicationUserAsync("Inspector");
        var adminId = await SeedApplicationUserAsync("Admin");
        var angebotId = await SeedAngebotAsync(inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.AngebotReviewComments.Add(AngebotReviewComment.Create(angebotId, adminId, "First"));
            await writeContext.SaveChangesAsync();

            writeContext.AngebotReviewComments.Add(AngebotReviewComment.Create(angebotId, adminId, "Second"));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var queries = new AngebotReviewCommentQueries(readContext);

        var result = await queries.GetForAngebotAsync(angebotId, CancellationToken.None);

        Assert.Equal(["First", "Second"], result.Select(c => c.Comment));
    }

    [Fact]
    public async Task GetForAngebotAsync_ReturnsEmptyWhenThereAreNoComments()
    {
        var inspectorId = await SeedApplicationUserAsync("Inspector");
        var angebotId = await SeedAngebotAsync(inspectorId);

        await using var context = fixture.CreateContext();
        var queries = new AngebotReviewCommentQueries(context);

        Assert.Empty(await queries.GetForAngebotAsync(angebotId, CancellationToken.None));
    }
}
