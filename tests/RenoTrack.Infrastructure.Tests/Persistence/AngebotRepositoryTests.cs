using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class-specific behavior — distinct from AngebotPersistenceTests (Slice 1), which
/// proves raw DbContext round-tripping of the full Sections/Items tree. These tests prove
/// AngebotRepository's own AddAsync/GetByIdAsync/HasActiveAngebotForLeadAsync contract.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class AngebotRepositoryTests(RenoTrackDbContextFixture fixture)
{
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    [Fact]
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheAngebot()
    {
        var leadId = await SeedLeadAsync();
        await using var context = fixture.CreateContext();
        var repository = new AngebotRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var angebot = Angebot.Create(leadId, inspectionId: null, angebotNumber: "ANG-2026-10001", createdByInspectorId: 5);

        await repository.AddAsync(angebot, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote.SingleOrDefaultAsync(a => a.Id == angebot.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("ANG-2026-10001", reloaded.AngebotNumber);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChangesAsync_PersistsNothing()
    {
        var leadId = await SeedLeadAsync();

        await using (var context = fixture.CreateContext())
        {
            var repository = new AngebotRepository(context);
            var angebot = Angebot.Create(leadId, null, "ANG-2026-10002", 5);

            await repository.AddAsync(angebot, CancellationToken.None);
            // Deliberately no call to IUnitOfWork.SaveChangesAsync here.
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote.SingleOrDefaultAsync(a => a.AngebotNumber == "ANG-2026-10002");
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsTheFullSectionsAndItemsTree()
    {
        var leadId = await SeedLeadAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-10003", 5);
        var section = angebot.AddSection("Pos. 1", sortOrder: 1);
        angebot.AddItemToSection(section, "Bodenbelag", 10m, ItemUnit.SquareMeter(), Money.FromExact(20.00m), VatRate.Standard);

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new AngebotRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(angebot, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var readRepository = new AngebotRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(angebot.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        var reloadedSection = Assert.Single(reloaded.Sections);
        Assert.Equal("Pos. 1", reloadedSection.Title);
        var reloadedItem = Assert.Single(reloadedSection.Items);
        Assert.Equal("Bodenbelag", reloadedItem.Description);
    }

    [Fact]
    public async Task GetByIdAsync_WhenAngebotDoesNotExist_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        var repository = new AngebotRepository(context);

        var result = await repository.GetByIdAsync(-1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddingASectionAndItemToAnAggregateLoadedViaGetByIdAsync_IsPersistedBySaveChangesAsyncAlone()
    {
        var leadId = await SeedLeadAsync();
        var angebot = Angebot.Create(leadId, null, "ANG-2026-10004", 5);

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new AngebotRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(angebot, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var updateRepository = new AngebotRepository(updateContext);
            var updateUnitOfWork = new UnitOfWork(updateContext);

            var loaded = await updateRepository.GetByIdAsync(angebot.Id, CancellationToken.None);
            Assert.NotNull(loaded);

            var section = loaded.AddSection("Pos. 1", 1);
            loaded.AddItemToSection(section, "Item", 1m, ItemUnit.Piece(), Money.FromExact(50.00m), VatRate.Standard);
            // No repository method exists (or is needed) to persist this — SaveChangesAsync alone.
            await updateUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Angebote
            .Include(a => a.Sections)
            .ThenInclude(s => s.Items)
            .SingleAsync(a => a.Id == angebot.Id);
        var reloadedSection = Assert.Single(reloaded.Sections);
        var reloadedItem = Assert.Single(reloadedSection.Items);
        Assert.Equal("Item", reloadedItem.Description);
    }

    [Theory]
    [InlineData(AngebotStatus.Draft, true)]
    [InlineData(AngebotStatus.CustomerApproved, false)]
    [InlineData(AngebotStatus.CustomerRejected, false)]
    public async Task HasActiveAngebotForLeadAsync_MatchesStateMachine24sNonTerminalDefinition(AngebotStatus status, bool expectedActive)
    {
        var leadId = await SeedLeadAsync();
        var angebot = Angebot.Create(leadId, null, $"ANG-2026-{(int)status:D5}", 5);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();

            // Status has a private setter in the Domain — driving it here via EF's own change
            // tracker (not a Domain transition method) is the only way to reach a terminal state
            // for this test, since RecordCustomerApproval/Rejection both require Status == Sent.
            writeContext.Entry(angebot).Property(nameof(Angebot.Status)).CurrentValue = status;
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var repository = new AngebotRepository(readContext);

        var isActive = await repository.HasActiveAngebotForLeadAsync(leadId, CancellationToken.None);

        Assert.Equal(expectedActive, isActive);
    }

    [Fact]
    public async Task HasActiveAngebotForLeadAsync_WhenLeadHasNoAngebot_ReturnsFalse()
    {
        var leadId = await SeedLeadAsync();
        await using var context = fixture.CreateContext();
        var repository = new AngebotRepository(context);

        var isActive = await repository.HasActiveAngebotForLeadAsync(leadId, CancellationToken.None);

        Assert.False(isActive);
    }
}
