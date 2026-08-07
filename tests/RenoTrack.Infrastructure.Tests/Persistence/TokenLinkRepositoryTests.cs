using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Repository-class behaviour, distinct from TokenLinkPersistenceTests' raw DbContext round-trip:
/// the AddAsync/SaveChangesAsync split, the token lookup contract, and the tracking guarantee the
/// decision endpoint will depend on.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class TokenLinkRepositoryTests(RenoTrackDbContextFixture fixture)
{
    private static string NewToken() => $"tok-{Guid.NewGuid():N}";

    private static TokenLink NewLink(string token) =>
        TokenLink.Create(TokenLinkEntityType.Angebot, entityId: 7, token, DateTime.UtcNow.AddDays(30));

    [Fact]
    public async Task AddAsync_FollowedBySaveChangesAsync_PersistsTheLink()
    {
        var token = NewToken();
        await using var context = fixture.CreateContext();
        var repository = new TokenLinkRepository(context);
        var unitOfWork = new UnitOfWork(context);

        await repository.AddAsync(NewLink(token), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        Assert.NotNull(await readContext.TokenLinks.SingleOrDefaultAsync(t => t.Token == token));
    }

    /// <summary>
    /// Persisting stays exclusively IUnitOfWork's job — the same contract every other repository in
    /// this project is pinned against.
    /// </summary>
    [Fact]
    public async Task AddAsync_WithoutSaveChanges_PersistsNothing()
    {
        var token = NewToken();
        await using (var context = fixture.CreateContext())
        {
            await new TokenLinkRepository(context).AddAsync(NewLink(token), CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        Assert.Null(await readContext.TokenLinks.SingleOrDefaultAsync(t => t.Token == token));
    }

    [Fact]
    public async Task FindByTokenAsync_ReturnsTheMatchingLink()
    {
        var token = NewToken();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(NewLink(token));
            await writeContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var found = await new TokenLinkRepository(context).FindByTokenAsync(token, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(token, found.Token);
    }

    /// <summary>
    /// An unknown token is an ordinary outcome on a public endpoint — including for a caller
    /// guessing at random — so the contract is null, not an exception.
    /// </summary>
    [Fact]
    public async Task FindByTokenAsync_ForAnUnknownToken_ReturnsNull()
    {
        await using var context = fixture.CreateContext();

        Assert.Null(await new TokenLinkRepository(context).FindByTokenAsync(NewToken(), CancellationToken.None));
    }

    /// <summary>
    /// The lookup must be exact. Pinned because a token is a credential: any prefix or
    /// case-insensitive match would shrink the search space an attacker has to cover, and SQL
    /// Server's default collation is case-insensitive, so this is a real hazard rather than a
    /// theoretical one — the column's binary content is what must match.
    /// </summary>
    [Fact]
    public async Task FindByTokenAsync_DoesNotMatchAPrefixOfARealToken()
    {
        var token = NewToken();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(NewLink(token));
            await writeContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var repository = new TokenLinkRepository(context);

        Assert.Null(await repository.FindByTokenAsync(token[..^1], CancellationToken.None));
    }

    /// <summary>
    /// The loaded instance must be tracked: this project has no UpdateAsync anywhere, so Slice 4's
    /// MarkUsed() would silently never persist if this read were AsNoTracking.
    /// </summary>
    [Fact]
    public async Task FindByTokenAsync_ReturnsATrackedInstance_SoMutationsPersist()
    {
        var token = NewToken();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(NewLink(token));
            await writeContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new TokenLinkRepository(context);
            var unitOfWork = new UnitOfWork(context);
            var link = await repository.FindByTokenAsync(token, CancellationToken.None);

            link!.MarkUsed();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Token == token);
        Assert.NotNull(reloaded.UsedAt);
    }
}
