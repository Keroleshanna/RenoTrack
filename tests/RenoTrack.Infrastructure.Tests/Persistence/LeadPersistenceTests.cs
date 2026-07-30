using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class LeadPersistenceTests(RenoTrackDbContextFixture fixture)
{
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
        var lead = Lead.Create("Max Klein", "0176 9999999", "max@example.com", LeadSource.Phone);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Leads.Add(lead);
            await writeContext.SaveChangesAsync();
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var toUpdate = await updateContext.Leads.SingleAsync(l => l.Id == lead.Id);
            toUpdate.AssignInspector(7);
            toUpdate.MarkInspectionScheduled();
            await updateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleAsync(l => l.Id == lead.Id);

        Assert.Equal(7, reloaded.AssignedInspectorId);
        Assert.Equal(LeadStatus.InspectionScheduled, reloaded.Status);
    }
}
