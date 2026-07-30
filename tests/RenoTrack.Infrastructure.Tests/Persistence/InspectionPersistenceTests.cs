using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class InspectionPersistenceTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task AddingAnInspectionWithAPhoto_PersistsAndReloadsThePhotoThroughTheBackingField()
    {
        var inspection = Inspection.Schedule(leadId: 1, scheduledAt: new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc), inspectorId: 5);
        inspection.AddPhoto("inspections/1/front-door.jpg", "Front door");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Inspections.Add(inspection);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Inspections
            .Include(i => i.Photos)
            .SingleAsync(i => i.Id == inspection.Id);

        Assert.Equal(1, reloaded.LeadId);
        Assert.Equal(5, reloaded.InspectorId);
        var photo = Assert.Single(reloaded.Photos);
        Assert.Equal("inspections/1/front-door.jpg", photo.FileUrl);
        Assert.Equal("Front door", photo.Caption);
    }

    [Fact]
    public async Task CompletingAnInspection_PersistsCompletedAt()
    {
        var inspection = Inspection.Schedule(1, new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc), 5);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Inspections.Add(inspection);
            await writeContext.SaveChangesAsync();
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var toComplete = await updateContext.Inspections.SingleAsync(i => i.Id == inspection.Id);
            toComplete.Complete();
            await updateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Inspections.SingleAsync(i => i.Id == inspection.Id);

        Assert.NotNull(reloaded.CompletedAt);
    }
}
