using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class LeadPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Lead.AssignedInspectorId is a real FK as of Slice 15 — needs an actually-persisted ApplicationUser row.</summary>
    private async Task<int> SeedApplicationUserAsync()
    {
        var user = new ApplicationUser { Name = "Test Inspector" };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task AddingALead_PersistsAndReloadsAllFields()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website, "Musterstr. 1", "Wants a quote");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Leads.Add(lead);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleAsync(l => l.Id == lead.Id);

        Assert.Equal("Jane Doe", reloaded.Name);
        Assert.Equal("0176 1234567", reloaded.Phone);
        Assert.Equal("jane@example.com", reloaded.Email);
        Assert.Equal("Musterstr. 1", reloaded.Address);
        Assert.Equal("Wants a quote", reloaded.Notes);
        Assert.Equal(LeadSource.Website, reloaded.Source);
        Assert.Equal(LeadStatus.New, reloaded.Status);
        Assert.Null(reloaded.AssignedInspectorId);
    }

    [Fact]
    public async Task AssigningAnInspectorAndTransitioningStatus_PersistsBothChanges()
    {
        var inspectorId = await SeedApplicationUserAsync();
        var lead = Lead.Create("Max Klein", "0176 9999999", "max@example.com", LeadSource.Phone);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Leads.Add(lead);
            await writeContext.SaveChangesAsync();
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var toUpdate = await updateContext.Leads.SingleAsync(l => l.Id == lead.Id);
            toUpdate.AssignInspector(inspectorId);
            toUpdate.MarkInspectionScheduled();
            await updateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleAsync(l => l.Id == lead.Id);

        Assert.Equal(inspectorId, reloaded.AssignedInspectorId);
        Assert.Equal(LeadStatus.InspectionScheduled, reloaded.Status);
    }

    [Fact]
    public async Task AssignedInspectorIdForeignKey_RejectsANonExistentUser()
    {
        var lead = Lead.Create("Max Klein", "0176 9999999", "max@example.com", LeadSource.Phone);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Leads.Add(lead);
            await writeContext.SaveChangesAsync();
        }

        await using var updateContext = fixture.CreateContext();
        var toUpdate = await updateContext.Leads.SingleAsync(l => l.Id == lead.Id);
        toUpdate.AssignInspector(999_999);

        await Assert.ThrowsAsync<DbUpdateException>(() => updateContext.SaveChangesAsync());
    }
}
