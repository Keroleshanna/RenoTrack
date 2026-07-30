using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class-specific behavior — distinct from InspectionPersistenceTests (Slice 1),
/// which proves raw DbContext round-tripping including the Photos backing-field navigation.
/// These tests prove InspectionRepository's own AddAsync/GetByIdAsync contract, including that
/// GetByIdAsync eagerly loads Photos (Inspection's one difference from LeadRepository) and that
/// a newly-added photo on an already-loaded aggregate is picked up by SaveChangesAsync alone.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InspectionRepositoryTests(RenoTrackDbContextFixture fixture)
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
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheInspection()
    {
        var leadId = await SeedLeadAsync();
        await using var context = fixture.CreateContext();
        var repository = new InspectionRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc), inspectorId: 3);

        await repository.AddAsync(inspection, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Inspections.SingleOrDefaultAsync(i => i.Id == inspection.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded.InspectorId);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChangesAsync_PersistsNothing()
    {
        var leadId = await SeedLeadAsync();
        Inspection inspection;

        await using (var context = fixture.CreateContext())
        {
            var repository = new InspectionRepository(context);
            inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc), inspectorId: 4);

            await repository.AddAsync(inspection, CancellationToken.None);
            // Deliberately no call to IUnitOfWork.SaveChangesAsync here.
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Inspections.SingleOrDefaultAsync(i => i.LeadId == leadId && i.InspectorId == 4);
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedInspectionWithPhotos()
    {
        var leadId = await SeedLeadAsync();
        var inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc), inspectorId: 5);
        inspection.AddPhoto("inspections/1/front-door.jpg", "Front door");

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new InspectionRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(inspection, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var readRepository = new InspectionRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(inspection.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(leadId, reloaded.LeadId);
        var photo = Assert.Single(reloaded.Photos);
        Assert.Equal("inspections/1/front-door.jpg", photo.FileUrl);
        Assert.Equal("Front door", photo.Caption);
    }

    [Fact]
    public async Task GetByIdAsync_WhenInspectionDoesNotExist_ReturnsNull()
    {
        await using var context = fixture.CreateContext();
        var repository = new InspectionRepository(context);

        var result = await repository.GetByIdAsync(-1, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AddingAPhotoToAnAggregateLoadedViaGetByIdAsync_IsPersistedBySaveChangesAsyncAlone()
    {
        var leadId = await SeedLeadAsync();
        var inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 23, 9, 0, 0, DateTimeKind.Utc), inspectorId: 6);

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new InspectionRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            await writeRepository.AddAsync(inspection, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var updateRepository = new InspectionRepository(updateContext);
            var updateUnitOfWork = new UnitOfWork(updateContext);

            var loaded = await updateRepository.GetByIdAsync(inspection.Id, CancellationToken.None);
            Assert.NotNull(loaded);

            loaded.AddPhoto("inspections/1/back-door.jpg", "Back door");
            // No repository method exists (or is needed) to persist this — SaveChangesAsync alone.
            await updateUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Inspections
            .Include(i => i.Photos)
            .SingleAsync(i => i.Id == inspection.Id);
        var photo = Assert.Single(reloaded.Photos);
        Assert.Equal("inspections/1/back-door.jpg", photo.FileUrl);
    }
}
