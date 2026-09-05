using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The invariant Slice 6 rests on, proven against real SQL Server: <b>at most one usable token link
/// per Angebot</b>, however many re-issues race for it (FR-6.1a, <b>D99</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>These tests exist because the first design was wrong and reasoning did not catch it.</b> It
/// claimed D96's <c>UsedAt</c> token already serialised two concurrent re-issues; it does not,
/// because EF puts a token's <i>original</i> value in the <c>WHERE</c> clause and a re-issue never
/// writes <c>UsedAt</c>. Both would have matched, both committed, and two live credentials would
/// have existed. So these assert the <b>invariant</b> — count the usable links — rather than
/// inspecting the mechanism that is supposed to produce it.
/// </para>
/// <para>
/// Real LocalDB, never InMemory (D40): an in-memory provider has no <c>WHERE</c> clause to miss,
/// so it cannot fail this test even with the concurrency token removed.
/// </para>
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class TokenLinkReissueConcurrencyTests(RenoTrackDbContextFixture fixture)
{
    private static string NewToken() => $"tok-{Guid.NewGuid():N}";

    /// <summary>Each test owns its own entity id, so a shared database cannot cross-contaminate.</summary>
    private static int NextEntityId() => Random.Shared.Next(100_000, 999_999);

    private static TokenLink LinkFor(int entityId, TimeSpan? lifetime = null) =>
        TokenLink.Create(
            TokenLinkEntityType.Angebot,
            entityId,
            NewToken(),
            DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(30)));

    private async Task<TokenLink> SeedLinkAsync(int entityId, TimeSpan? lifetime = null)
    {
        var link = LinkFor(entityId, lifetime);
        await using var context = fixture.CreateContext();
        context.TokenLinks.Add(link);
        await context.SaveChangesAsync();
        return link;
    }

    /// <summary>Counts what the invariant is actually about: links a customer could still use.</summary>
    private async Task<List<TokenLink>> UsableLinksAsync(int entityId)
    {
        var now = DateTime.UtcNow;
        await using var context = fixture.CreateContext();
        return await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == entityId)
            .Where(t => t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync();
    }

    private async Task<int> LinkCountAsync(int entityId)
    {
        await using var context = fixture.CreateContext();
        return await context.TokenLinks
            .CountAsync(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == entityId);
    }

    /// <summary>
    /// One re-issue, done exactly as the handler does it: supersede the current link and add the
    /// replacement, both in one <c>SaveChangesAsync</c> so EF batches them into one transaction.
    /// Returns the exception if the commit lost a race.
    /// </summary>
    private async Task<Exception?> ReissueAsync(RenoTrackDbContext context, int entityId, TaskCompletionSource? gate = null)
    {
        var current = await context.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == entityId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstAsync();

        current.Expire();
        context.TokenLinks.Add(LinkFor(entityId));

        if (gate is not null)
        {
            await gate.Task;
        }

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

    // ---- The sequential case -----------------------------------------------------------------

    [Fact]
    public async Task AReissue_LeavesTheOldLinkPersistedButUnusable_AndExactlyOneUsableLink()
    {
        var entityId = NextEntityId();
        var original = await SeedLinkAsync(entityId);

        await using (var context = fixture.CreateContext())
        {
            Assert.Null(await ReissueAsync(context, entityId));
        }

        // Never deleted — the old row is retired, not removed.
        Assert.Equal(2, await LinkCountAsync(entityId));

        await using (var readContext = fixture.CreateContext())
        {
            var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == original.Id);
            Assert.True(reloaded.IsExpired(DateTime.UtcNow));

            // And never marked used: UsedAt keeps its single meaning (BR-4).
            Assert.Null(reloaded.UsedAt);
        }

        var usable = await UsableLinksAsync(entityId);
        Assert.Single(usable);
        Assert.NotEqual(original.Token, usable[0].Token);
    }

    // ---- The race this slice exists to survive -----------------------------------------------

    /// <summary>
    /// <b>N concurrent re-issues: exactly one 200-equivalent, N−1 conflicts, exactly one usable
    /// link.</b> All readers load the same original before any writer commits, so the commits
    /// genuinely race rather than follow one another.
    /// </summary>
    /// <remarks>
    /// The loser's replacement must never survive. EF batches the <c>UPDATE</c> and the
    /// <c>INSERT</c> into one transaction, so a failed concurrency check rolls both back — which is
    /// why the usable count is asserted <i>and</i> the total row count, since a leaked orphan would
    /// show up in the second even when the first looked right.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task ConcurrentReissues_LeaveExactlyOneWinnerAndExactlyOneUsableLink(int callers)
    {
        var entityId = NextEntityId();
        await SeedLinkAsync(entityId);

        // A DbContext is not thread-safe, so each caller gets its own — sharing one would be a test
        // bug rather than a race.
        var contexts = Enumerable.Range(0, callers).Select(_ => fixture.CreateContext()).ToList();

        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = contexts.Select(context => ReissueAsync(context, entityId, gate)).ToList();
            gate.SetResult();

            var outcomes = await Task.WhenAll(attempts);

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Equal(callers - 1, outcomes.Count(outcome => outcome is DbUpdateConcurrencyException));

            // The invariant itself.
            Assert.Single(await UsableLinksAsync(entityId));

            // One original plus exactly one replacement: the losers left nothing behind.
            Assert.Equal(2, await LinkCountAsync(entityId));
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A concurrency test passing once proves the race <i>can</i> resolve correctly, not that it
    /// always does (CLAUDE.md §14 — D55 hid behind a single green run). This drives the same race
    /// repeatedly.
    /// </summary>
    [Fact]
    public async Task ConcurrentReissues_HoldTheInvariantAcrossRepeatedRuns()
    {
        for (var round = 0; round < 10; round++)
        {
            var entityId = NextEntityId();
            await SeedLinkAsync(entityId);

            await using var first = fixture.CreateContext();
            await using var second = fixture.CreateContext();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = new[] { ReissueAsync(first, entityId, gate), ReissueAsync(second, entityId, gate) };
            gate.SetResult();

            var outcomes = await Task.WhenAll(attempts);

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Single(await UsableLinksAsync(entityId));
            Assert.Equal(2, await LinkCountAsync(entityId));
        }
    }

    /// <summary>
    /// Q2's case, and the one the "always write" rule protects: a link that lapsed naturally is
    /// still re-issuable, and the write still carries the concurrency predicate — so two concurrent
    /// re-issues of a lapsed link are serialised exactly as two re-issues of a live one.
    /// </summary>
    [Fact]
    public async Task ConcurrentReissuesOfAnAlreadyLapsedLink_AreStillSerialised()
    {
        var entityId = NextEntityId();
        await SeedLinkAsync(entityId, TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        Assert.Empty(await UsableLinksAsync(entityId));

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { ReissueAsync(first, entityId, gate), ReissueAsync(second, entityId, gate) };
        gate.SetResult();

        var outcomes = await Task.WhenAll(attempts);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DbUpdateConcurrencyException);
        Assert.Single(await UsableLinksAsync(entityId));
    }

    // ---- The other race, still safe -----------------------------------------------------------

    /// <summary>
    /// <b>Decision versus re-issue, unchanged by this slice.</b> The decision writes
    /// <c>UsedAt</c> and the re-issue writes <c>ExpiresAt</c>; both are concurrency tokens, so
    /// whichever commits second matches zero rows. Exactly one succeeds, and the two outcomes stay
    /// distinguishable: either the customer's decision stands and no replacement exists, or the
    /// re-issue stands and the decision was refused.
    /// </summary>
    [Fact]
    public async Task ADecisionRacingAReissue_LeavesExactlyOneWinnerAndACoherentState()
    {
        var entityId = NextEntityId();
        var original = await SeedLinkAsync(entityId);

        await using var decisionContext = fixture.CreateContext();
        await using var reissueContext = fixture.CreateContext();

        var deciding = await decisionContext.TokenLinks.SingleAsync(t => t.Id == original.Id);
        var reissuing = await reissueContext.TokenLinks.SingleAsync(t => t.Id == original.Id);

        deciding.MarkUsed();
        reissuing.Expire();
        reissueContext.TokenLinks.Add(LinkFor(entityId));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> CommitAsync(RenoTrackDbContext context)
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

        var attempts = new[] { CommitAsync(decisionContext), CommitAsync(reissueContext) };
        gate.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DbUpdateConcurrencyException);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.TokenLinks.SingleAsync(t => t.Id == original.Id);

        if (reloaded.UsedAt is not null)
        {
            // The decision won: no replacement was created, so the customer's answer is the last
            // word and nothing invented a second credential behind it.
            Assert.Equal(1, await LinkCountAsync(entityId));
        }
        else
        {
            // The re-issue won: the old link is superseded and exactly one usable link exists.
            Assert.True(reloaded.IsExpired(DateTime.UtcNow));
            Assert.Single(await UsableLinksAsync(entityId));
        }
    }

    /// <summary>
    /// A decision arriving through a link that a re-issue already superseded must not succeed. The
    /// aggregate's own expiry guard refuses it, and the database's predicate would refuse it too —
    /// so the customer's old email cannot decide an Angebot behind a live replacement.
    /// </summary>
    [Fact]
    public async Task ADecisionThroughASupersededLink_CannotSucceed()
    {
        var entityId = NextEntityId();
        var original = await SeedLinkAsync(entityId);

        await using (var context = fixture.CreateContext())
        {
            Assert.Null(await ReissueAsync(context, entityId));
        }

        await using var decisionContext = fixture.CreateContext();
        var superseded = await decisionContext.TokenLinks.SingleAsync(t => t.Id == original.Id);

        Assert.Throws<InvalidOperationException>(superseded.MarkUsed);

        await using var readContext = fixture.CreateContext();
        Assert.Null((await readContext.TokenLinks.SingleAsync(t => t.Id == original.Id)).UsedAt);
    }
}
