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

    /// <summary>
    /// D96: <c>UsedAt</c> is an optimistic-concurrency token, so a second unit of work that read
    /// the link while it was still unused cannot consume it after someone else already has.
    /// </summary>
    /// <remarks>
    /// The two contexts are opened and both read *before* either writes, which is the whole point:
    /// this reproduces the interleaving a real double-click produces, not a sequential re-use that
    /// <c>MarkUsed()</c>'s own in-memory guard would already catch. Before this change both
    /// <c>SaveChangesAsync</c> calls succeeded and the link was consumed twice.
    /// </remarks>
    [Fact]
    public async Task AStaleUnitOfWorkCannotConsumeALinkThatWasAlreadyConsumed()
    {
        var link = NewLink();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(link);
            await writeContext.SaveChangesAsync();
        }

        await using var winner = fixture.CreateContext();
        await using var loser = fixture.CreateContext();

        var winnerLink = await winner.TokenLinks.SingleAsync(t => t.Id == link.Id);
        var loserLink = await loser.TokenLinks.SingleAsync(t => t.Id == link.Id);

        winnerLink.MarkUsed();
        await winner.SaveChangesAsync();

        loserLink.MarkUsed();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync());

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == link.Id);
        Assert.Equal(winnerLink.UsedAt, reloaded.UsedAt);
    }

    /// <summary>
    /// The same race driven genuinely in parallel rather than by a deterministic interleaving, and
    /// repeated — CLAUDE.md §14: a concurrency test that has passed once proves only that the race
    /// <i>can</i> resolve correctly. D55 is the precedent: a race proof that passed when written
    /// then failed about two runs in three once it was repeated.
    /// </summary>
    /// <remarks>
    /// Asserts the property that actually matters and that the sequential test above cannot reach:
    /// <b>exactly one</b> of the two attempts commits. A run where both threads happen to serialize
    /// is still a valid run of this test — it just exercises the same guarantee through the other
    /// interleaving — so there is no flakiness to tolerate here and nothing is retried.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ExactlyOneOfTwoSimultaneousConsumersWins(int attempt)
    {
        // The parameter exists only to make xUnit run this five times as five distinct cases; it is
        // read here so the "theory does not use its parameter" analyzer stays satisfied.
        _ = attempt;

        var link = NewLink();
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.TokenLinks.Add(link);
            await writeContext.SaveChangesAsync();
        }

        // Both contexts read, and both mutate, before either writes — so the two SaveChangesAsync
        // calls genuinely race rather than merely follow one another.
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var firstLink = await first.TokenLinks.SingleAsync(t => t.Id == link.Id);
        var secondLink = await second.TokenLinks.SingleAsync(t => t.Id == link.Id);
        firstLink.MarkUsed();
        secondLink.MarkUsed();

        // An asynchronous gate rather than a Barrier: both consumers await the same signal without
        // occupying a pool thread, so the test cannot deadlock on a constrained thread pool.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> ConsumeAsync(RenoTrackDbContext context)
        {
            await gate.Task;

            try
            {
                await context.SaveChangesAsync();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var firstConsumer = ConsumeAsync(first);
        var secondConsumer = ConsumeAsync(second);
        gate.SetResult();

        var outcomes = await Task.WhenAll(firstConsumer, secondConsumer);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DbUpdateConcurrencyException);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == link.Id);
        Assert.NotNull(reloaded.UsedAt);
    }
}
