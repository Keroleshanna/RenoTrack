using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the Project detail projection is genuinely SQL-translatable — the thing a fake cannot
/// establish, and the reason `ICatalogItemQueries` got the same treatment in Phase 3 Slice 9. The
/// three-table join exists because `Project` deliberately holds no navigation property to
/// `Customer` or `Angebot`, so there is nothing to `Include`.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ProjectQueriesTests(RenoTrackDbContextFixture fixture)
{
    private async Task<(int ProjectId, int LeadId, int AngebotId, string AngebotNumber)> SeedProjectAsync(
        RenoTrackDbContext context,
        ProjectStatus status = ProjectStatus.Active)
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", $"{Guid.NewGuid():N}@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var user = new ApplicationUser { Name = "Test Inspector" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var angebotNumber = $"ANG-{Guid.NewGuid():N}"[..18];
        var angebot = Angebot.Create(lead.Id, inspectionId: null, angebotNumber, user.Id);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        if (status == ProjectStatus.OnHold)
        {
            project.PutOnHold();
        }

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, lead.Id, angebot.Id, angebotNumber);
    }

    [Fact]
    public async Task GetByIdAsyncProjectsEveryFieldAcrossThreeTables()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seeded.ProjectId, result.Id);
        Assert.Equal(ProjectStatus.Active, result.Status);
        Assert.Equal(25_673.36m, result.AgreedTotal);
        Assert.Null(result.CompletedAt);
        Assert.Equal("M. Klein", result.CustomerName);
        Assert.Equal(seeded.LeadId, result.LeadId);
        Assert.Null(result.InspectionId);
        Assert.Equal(seeded.AngebotId, result.AngebotId);
        Assert.Equal(seeded.AngebotNumber, result.AngebotNumber);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullForAnUnknownProject()
    {
        await using var context = fixture.CreateContext();

        var result = await new ProjectQueries(context).GetByIdAsync(999_999_999, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// Status round-trips through the string column and the projection together — the projection
    /// reads the converted property, so a broken converter would surface here as well as in
    /// `ProjectPersistenceTests`.
    /// </summary>
    [Fact]
    public async Task GetByIdAsyncReflectsANonDefaultStatus()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context, ProjectStatus.OnHold);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ProjectStatus.OnHold, result.Status);
    }
}
