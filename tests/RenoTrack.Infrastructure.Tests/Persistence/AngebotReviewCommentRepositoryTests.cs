using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class-specific behavior — distinct from AngebotReviewCommentPersistenceTests
/// (Slice 1), which proves raw DbContext round-tripping. AddAsync is the only method on this
/// interface (no GetByIdAsync), so the contract to prove is narrower than every prior slice.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class AngebotReviewCommentRepositoryTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Angebot.CreatedByInspectorId is a real FK as of Slice 15 — needs an actually-persisted ApplicationUser row.</summary>
    private async Task<int> SeedApplicationUserAsync()
    {
        var user = new ApplicationUser { Name = "Test Inspector" };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedAngebotAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        await using var leadContext = fixture.CreateContext();
        leadContext.Leads.Add(lead);
        await leadContext.SaveChangesAsync();

        var inspectorId = await SeedApplicationUserAsync();

        var angebot = Angebot.Create(lead.Id, null, $"ANG-2026-2{lead.Id:D4}", createdByInspectorId: inspectorId);
        await using var angebotContext = fixture.CreateContext();
        angebotContext.Angebote.Add(angebot);
        await angebotContext.SaveChangesAsync();

        return angebot.Id;
    }

    [Fact]
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheComment()
    {
        var angebotId = await SeedAngebotAsync();
        var adminUserId = await SeedApplicationUserAsync();
        await using var context = fixture.CreateContext();
        var repository = new AngebotReviewCommentRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var comment = AngebotReviewComment.Create(angebotId, adminUserId, comment: "Please adjust the VAT rate.");

        await repository.AddAsync(comment, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AngebotReviewComments.SingleOrDefaultAsync(c => c.Id == comment.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Please adjust the VAT rate.", reloaded.Comment);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChangesAsync_PersistsNothing()
    {
        var angebotId = await SeedAngebotAsync();
        var adminUserId = await SeedApplicationUserAsync();

        await using (var context = fixture.CreateContext())
        {
            var repository = new AngebotReviewCommentRepository(context);
            var comment = AngebotReviewComment.Create(angebotId, adminUserId, comment: "Never saved.");

            await repository.AddAsync(comment, CancellationToken.None);
            // Deliberately no call to IUnitOfWork.SaveChangesAsync here.
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AngebotReviewComments.SingleOrDefaultAsync(c => c.Comment == "Never saved.");
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task AddAsync_PersistedViaOneContextInstance_IsVisibleFromAnother()
    {
        var angebotId = await SeedAngebotAsync();
        var adminUserId = await SeedApplicationUserAsync();

        await using (var writeContext = fixture.CreateContext())
        {
            var writeRepository = new AngebotReviewCommentRepository(writeContext);
            var writeUnitOfWork = new UnitOfWork(writeContext);
            var comment = AngebotReviewComment.Create(angebotId, adminUserId, comment: "Cross-context comment.");
            await writeRepository.AddAsync(comment, CancellationToken.None);
            await writeUnitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AngebotReviewComments.SingleOrDefaultAsync(c => c.AngebotId == angebotId && c.AdminUserId == adminUserId);
        Assert.NotNull(reloaded);
        Assert.Equal("Cross-context comment.", reloaded.Comment);
    }
}
