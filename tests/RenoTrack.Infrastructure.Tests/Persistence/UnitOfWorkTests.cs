using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class UnitOfWorkTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChangesTrackedByTheSameDbContext()
    {
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website);
        context.Leads.Add(lead);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal("Jane Doe", reloaded.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoPendingChanges_DoesNotThrow()
    {
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var exception = await Record.ExceptionAsync(() => unitOfWork.SaveChangesAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SaveChangesAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // EF Core short-circuits SaveChangesAsync when nothing is tracked, without ever
        // checking the token — a real pending change is required to exercise cancellation.
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);
        context.Leads.Add(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.SaveChangesAsync(cts.Token));
    }

    /// <summary>
    /// D96: an optimistic-concurrency loss leaves this method as the Application layer's own
    /// <see cref="ConflictException"/>, which the API already maps to 409 — so the public decision
    /// endpoint's loser gets a deterministic 409 rather than an unmapped 500.
    /// </summary>
    /// <remarks>
    /// The translation cannot live in a handler: <c>DbUpdateConcurrencyException</c> is an EF Core
    /// type and <c>RenoTrack.Application</c> does not reference EF Core (CLAUDE.md §22).
    /// </remarks>
    [Fact]
    public async Task SaveChangesAsync_WhenAConcurrencyTokenLoses_ThrowsConflictException()
    {
        var link = TokenLink.Create(
            TokenLinkEntityType.Angebot,
            entityId: 1,
            $"tok-{Guid.NewGuid():N}",
            DateTime.UtcNow.AddDays(30));

        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.TokenLinks.Add(link);
            await seedContext.SaveChangesAsync();
        }

        await using var winner = fixture.CreateContext();
        await using var loser = fixture.CreateContext();
        var winnerLink = await winner.TokenLinks.SingleAsync(t => t.Id == link.Id);
        var loserLink = await loser.TokenLinks.SingleAsync(t => t.Id == link.Id);

        winnerLink.MarkUsed();
        await winner.SaveChangesAsync();

        loserLink.MarkUsed();

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => new UnitOfWork(loser).SaveChangesAsync(CancellationToken.None));

        // The inner exception survives, because D59 logs every mapped exception with its full stack
        // trace and a translation that discarded the cause would make that instruction worthless.
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
    }

    /// <summary>
    /// The message becomes the ProblemDetails <c>detail</c> (D59), and the first caller to reach
    /// this path is the anonymous public decision endpoint — so it must name no row, no table, no
    /// timestamp and no token.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ConcurrencyConflictMessage_NamesNothingAboutTheContendedRow()
    {
        var token = $"tok-{Guid.NewGuid():N}";
        var link = TokenLink.Create(TokenLinkEntityType.Angebot, entityId: 1, token, DateTime.UtcNow.AddDays(30));

        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.TokenLinks.Add(link);
            await seedContext.SaveChangesAsync();
        }

        await using var winner = fixture.CreateContext();
        await using var loser = fixture.CreateContext();
        var winnerLink = await winner.TokenLinks.SingleAsync(t => t.Id == link.Id);
        var loserLink = await loser.TokenLinks.SingleAsync(t => t.Id == link.Id);
        winnerLink.MarkUsed();
        await winner.SaveChangesAsync();
        loserLink.MarkUsed();

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => new UnitOfWork(loser).SaveChangesAsync(CancellationToken.None));

        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TokenLink", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(link.Id.ToString(), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only <see cref="DbUpdateConcurrencyException"/> is translated, never its
    /// <see cref="DbUpdateException"/> base. A unique-index violation is a defect, not a conflict a
    /// caller should be invited to retry, and must keep surfacing as an unmapped 500 with its stack
    /// trace intact.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AConstraintViolation_IsNotTranslatedIntoAConflict()
    {
        var token = $"tok-{Guid.NewGuid():N}";

        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.TokenLinks.Add(
                TokenLink.Create(TokenLinkEntityType.Angebot, 1, token, DateTime.UtcNow.AddDays(30)));
            await seedContext.SaveChangesAsync();
        }

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.TokenLinks.Add(
            TokenLink.Create(TokenLinkEntityType.Angebot, 2, token, DateTime.UtcNow.AddDays(30)));

        var exception = await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => new UnitOfWork(duplicateContext).SaveChangesAsync(CancellationToken.None));

        Assert.IsNotType<DbUpdateConcurrencyException>(exception);
    }
}
