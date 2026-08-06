using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Raw DbContext round-tripping and the real SQL constraints behind TokenLinkConfiguration —
/// against real LocalDB, never InMemory (D40), because the unique index and the string-stored enum
/// are exactly the things InMemory would not enforce.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class TokenLinkPersistenceTests(RenoTrackDbContextFixture fixture)
{
    private static string NewToken() => $"tok-{Guid.NewGuid():N}";

    private static TokenLink NewLink(string? token = null, TokenLinkEntityType entityType = TokenLinkEntityType.Angebot) =>
        TokenLink.Create(entityType, entityId: 1, token ?? NewToken(), DateTime.UtcNow.AddDays(30));

    [Fact]
    public async Task ATokenLinkRoundTripsWithEveryField()
    {
        var link = NewLink();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(link);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == link.Id);

        Assert.Equal(TokenLinkEntityType.Angebot, reloaded.EntityType);
        Assert.Equal(link.EntityId, reloaded.EntityId);
        Assert.Equal(link.Token, reloaded.Token);
        Assert.Null(reloaded.UsedAt);
    }

    /// <summary>
    /// ERD.md §3 lists this index as unique, and D60 gives the reason it matters beyond speed: two
    /// rows sharing a token would make the single public lookup path ambiguous.
    /// </summary>
    [Fact]
    public async Task TwoTokenLinksCannotShareAToken()
    {
        var token = NewToken();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(NewLink(token));
            await writeContext.SaveChangesAsync();
        }

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.TokenLinks.Add(NewLink(token));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    /// <summary>
    /// EntityType is stored as its name, not its ordinal — the readability reason ERD.md gives for
    /// every other enum column, and the reason D61 gives for the wire format. Read back through raw
    /// SQL rather than through EF, since EF's own converter would hide an ordinal.
    /// </summary>
    [Fact]
    public async Task EntityTypeIsStoredAsAString()
    {
        var link = NewLink(entityType: TokenLinkEntityType.Invoice);
        await using var context = fixture.CreateContext();
        context.TokenLinks.Add(link);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<string>($"SELECT EntityType AS Value FROM TokenLinks WHERE Id = {link.Id}")
            .SingleAsync();

        Assert.Equal(nameof(TokenLinkEntityType.Invoice), stored);
    }

    /// <summary>
    /// The polymorphic design (Architecture §7.2, ERD.md) means EntityId deliberately has no FK —
    /// no single column can reference both Angebote and Invoices. Pinned so a future "add the
    /// missing FK" tidy-up has to argue with a failing test first: TokenLinks is the one
    /// documented exception to CLAUDE.md §21's "add an FK wherever both tables exist".
    /// </summary>
    [Fact]
    public async Task AnEntityIdReferencingNoRealRowIsAccepted()
    {
        var link = TokenLink.Create(TokenLinkEntityType.Angebot, entityId: 999_999_999, NewToken(), DateTime.UtcNow.AddDays(30));

        await using var context = fixture.CreateContext();
        context.TokenLinks.Add(link);

        await context.SaveChangesAsync();

        Assert.True(link.Id > 0);
    }

    /// <summary>
    /// An expired row must still be *readable*. EF Core materialises entities through the same
    /// private constructor <c>Create</c> uses, so a time-dependent guard placed there would throw
    /// on load once the link lapsed — making expired links surface as 400 rather than the 410 the
    /// public endpoint owes them, and making the row effectively unreadable forever.
    ///
    /// This was a real defect, found by an integration test and not by inspection: the guard lived
    /// in the constructor until the first test read an expired row back. Expiry is set through SQL
    /// because the aggregate deliberately refuses to construct an already-expired link.
    /// </summary>
    [Fact]
    public async Task AnExpiredTokenLinkCanStillBeLoaded()
    {
        var link = NewLink();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(link);
            await writeContext.SaveChangesAsync();
            await writeContext.Database.ExecuteSqlAsync(
                $"UPDATE TokenLinks SET ExpiresAt = {DateTime.UtcNow.AddDays(-1)} WHERE Id = {link.Id}");
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == link.Id);

        Assert.True(reloaded.IsExpired(DateTime.UtcNow));
    }

    /// <summary>
    /// MarkUsed on an entity loaded from the database persists through SaveChangesAsync alone —
    /// there is no UpdateAsync anywhere in this project, so the decision endpoint (Slice 4)
    /// depends entirely on the change tracker seeing this mutation.
    /// </summary>
    [Fact]
    public async Task MarkUsedOnALoadedLinkPersistsViaSaveChangesAlone()
    {
        var link = NewLink();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(link);
            await writeContext.SaveChangesAsync();
        }

        await using (var mutateContext = fixture.CreateContext())
        {
            var loaded = await mutateContext.TokenLinks.SingleAsync(t => t.Id == link.Id);
            loaded.MarkUsed();
            await mutateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == link.Id);
        Assert.NotNull(reloaded.UsedAt);
    }
}
