using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class-specific behavior — distinct from LeadPersistenceTests (Slice 1), which
/// proves raw DbContext round-tripping of Lead's fields. These tests prove LeadRepository's own
/// AddAsync/GetByIdAsync contract: that AddAsync only stages a change (SaveChangesAsync remains
/// exclusively IUnitOfWork's responsibility, CLAUDE.md §4), that GetByIdAsync's not-found case
/// returns null, and that behavior is correct across separate DbContext instances.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class LeadRepositoryTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheLead()
    {
        await using var context = fixture.CreateContext();
        var repository = new LeadRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website);

        await repository.AddAsync(lead, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleOrDefaultAsync(l => l.Id == lead.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Jane Doe", reloaded.Name);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChangesAsync_PersistsNothing()
    {
        await using (var context = fixture.CreateContext())
        {
            var repository = new LeadRepository(context);
            var lead = Lead.Create("No Save", "0176 0000000", "nosave@example.com", LeadSource.Phone);

            await repository.AddAsync(lead, CancellationToken.None);
            // Deliberately no call to IUnitOfWork.SaveChangesAsync here.
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleOrDefaultAsync(l => l.Email == "nosave@example.com");
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedLead()
    {
        var lead = Lead.Create("Cross Context", "0176 5555555", "cross@example.com", LeadSource.Email);

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new LeadRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(lead, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var readRepository = new LeadRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(lead.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal("Cross Context", reloaded.Name);
        Assert.Equal("0176 5555555", reloaded.Phone);
        Assert.Equal("cross@example.com", reloaded.Email);
        Assert.Equal(LeadSource.Email, reloaded.Source);
    }

    [Fact]
    public async Task GetByIdAsync_WhenLeadDoesNotExist_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        var repository = new LeadRepository(context);

        var result = await repository.GetByIdAsync(-1, CancellationToken.None);

        Assert.Null(result);
    }
}
