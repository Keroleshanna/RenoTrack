using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class AngebotReviewCommentPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Angebot.LeadId is a real FK — needs an actually-persisted Lead row, not a hardcoded placeholder id.</summary>
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    /// <summary>Angebot.CreatedByInspectorId/AngebotReviewComment.AdminUserId are real FKs as of Slice 15.</summary>
    private async Task<int> SeedApplicationUserAsync(string name)
    {
        var user = new ApplicationUser { Name = name };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task AddingAReviewComment_PersistsAndReloadsAllFields()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync("Test Inspector");
        var adminUserId = await SeedApplicationUserAsync("Test Admin");
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00010", inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        var comment = AngebotReviewComment.Create(angebot.Id, adminUserId, comment: "Please adjust the VAT rate on section 2.");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.AngebotReviewComments.Add(comment);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AngebotReviewComments.SingleAsync(c => c.Id == comment.Id);

        Assert.Equal(angebot.Id, reloaded.AngebotId);
        Assert.Equal(adminUserId, reloaded.AdminUserId);
        Assert.Equal("Please adjust the VAT rate on section 2.", reloaded.Comment);
    }

    [Fact]
    public async Task AngebotIdForeignKey_RejectsANonExistentAngebot()
    {
        var adminUserId = await SeedApplicationUserAsync("Test Admin");
        var comment = AngebotReviewComment.Create(angebotId: 999_999, adminUserId, comment: "Orphaned comment");

        await using var writeContext = fixture.CreateContext();
        writeContext.AngebotReviewComments.Add(comment);

        await Assert.ThrowsAsync<DbUpdateException>(() => writeContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AdminUserIdForeignKey_RejectsANonExistentUser()
    {
        var leadId = await SeedLeadAsync();
        var inspectorId = await SeedApplicationUserAsync("Test Inspector");
        var angebot = Angebot.Create(leadId, null, "ANG-2026-00011", inspectorId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        var comment = AngebotReviewComment.Create(angebot.Id, adminUserId: 999_999, comment: "Orphaned admin reference");

        await using var commentContext = fixture.CreateContext();
        commentContext.AngebotReviewComments.Add(comment);

        await Assert.ThrowsAsync<DbUpdateException>(() => commentContext.SaveChangesAsync());
    }
}
