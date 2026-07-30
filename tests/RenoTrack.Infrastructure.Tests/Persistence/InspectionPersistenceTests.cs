using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class InspectionPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Inspection.LeadId is a real FK — every test needs an actually-persisted Lead row, not a hardcoded placeholder id.</summary>
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    /// <summary>Inspection.InspectorId is a real FK as of Slice 15 — needs an actually-persisted ApplicationUser row.</summary>
    private async Task<int> SeedApplicationUserAsync()
    {
        var user = new ApplicationUser { Name = "Test Inspector" };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task AddingAnInspectionWithAPhoto_PersistsAndReloadsThePhotoThroughTheBackingField()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var inspection = Inspection.Schedule(leadId, scheduledAt: new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc), inspectorId: inspectorId);
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

        Assert.Equal(leadId, reloaded.LeadId);
        Assert.Equal(inspectorId, reloaded.InspectorId);
        var photo = Assert.Single(reloaded.Photos);
        Assert.Equal("inspections/1/front-door.jpg", photo.FileUrl);
        Assert.Equal("Front door", photo.Caption);
    }

    [Fact]
    public async Task CompletingAnInspection_PersistsCompletedAt()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync();
        var inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc), inspectorId);

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

    [Fact]
    public async Task LeadIdForeignKey_RejectsANonExistentLead()
    {
        var inspectorId = await SeedApplicationUserAsync();
        var inspection = Inspection.Schedule(leadId: 999_999, new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc), inspectorId);

        await using var writeContext = fixture.CreateContext();
        writeContext.Inspections.Add(inspection);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task InspectorIdForeignKey_RejectsANonExistentUser()
    {
        var leadId = await SeedLeadAsync();
        var inspection = Inspection.Schedule(leadId, new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc), inspectorId: 999_999);

        await using var writeContext = fixture.CreateContext();
        writeContext.Inspections.Add(inspection);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }
}
