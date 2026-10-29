using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The invariants Admin editing (<b>D115</b>) rests on, proven against real SQL Server: an
/// Angebot's stored totals always agree with its lines, and nothing written by a request that
/// read an older state of the Angebot can land after the Angebot moved on.
/// </summary>
/// <remarks>
/// <para>
/// <b>These tests were written first and run against <c>main</c>'s model before
/// <c>Angebot.Version</c> existed, and every one of them failed there.</b> Both hazards were
/// found by reading the code during design review; the failures are what turned them from a
/// claim into a fact. They assert the <b>invariant</b> — reload and compare — rather than the
/// mechanism, so they keep meaning something if the mechanism is ever replaced.
/// </para>
/// <para>
/// Each contender stages its change on its own <see cref="RenoTrackDbContext"/> before any of
/// them commits, so "both read the same original row" is a property of the test's structure, not
/// of timing (the shape <c>TokenLinkReissueConcurrencyTests</c> established). Real LocalDB, never
/// InMemory (D40): an in-memory provider has no <c>WHERE</c> clause to miss.
/// </para>
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class AngebotConcurrencyTests(RenoTrackDbContextFixture fixture)
{
    private const decimal OriginalQuantity = 2m;
    private const string OriginalDescription = "Fliesen verlegen";

    private async Task<int> SeedAngebotWithTwoLinesAsync()
    {
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Phone);
        var user = new ApplicationUser { Name = "Test Inspector" };

        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.Leads.Add(lead);
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
        }

        // Unique per test: the shared database has a unique index on AngebotNumber.
        var angebot = Angebot.Create(lead.Id, null, $"ANG-T-{Guid.NewGuid():N}"[..30], user.Id);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, OriginalDescription, OriginalQuantity, ItemUnit.SquareMeter(), Money.FromExact(50.00m), VatRate.Standard);
        angebot.AddItemToSection(section, "Silikonfugen", OriginalQuantity, ItemUnit.LinearMeter(), Money.FromExact(10.00m), VatRate.Reduced);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Angebote.Add(angebot);
            await writeContext.SaveChangesAsync();
        }

        return angebot.Id;
    }

    private static async Task<Angebot> LoadAsync(RenoTrackDbContext context, int angebotId) =>
        (await new AngebotRepository(context).GetByIdAsync(angebotId, CancellationToken.None))!;

    private static (AngebotSection Section, AngebotItem Item) Line(Angebot angebot, int index)
    {
        var section = angebot.Sections.Single();
        return (section, section.Items.OrderBy(i => i.Id).ElementAt(index));
    }

    private static void ChangeQuantity(Angebot angebot, int lineIndex, decimal quantity)
    {
        var (section, item) = Line(angebot, lineIndex);
        angebot.UpdateItem(section, item, item.Description, quantity, item.Unit, item.UnitPrice, item.VatRate, item.Specification);
    }

    private static void ChangeDescription(Angebot angebot, int lineIndex, string description)
    {
        var (section, item) = Line(angebot, lineIndex);
        angebot.UpdateItem(section, item, description, item.Quantity, item.Unit, item.UnitPrice, item.VatRate, item.Specification);
    }

    /// <summary>The commit, and the only thing the gate releases. Returns the exception when this contender lost.</summary>
    private static async Task<Exception?> CommitAsync(RenoTrackDbContext context, Task gate)
    {
        await gate;

        try
        {
            await new UnitOfWork(context).SaveChangesAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?[]> RaceCommitsAsync(params RenoTrackDbContext[] contexts)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = contexts.Select(context => CommitAsync(context, gate.Task)).ToList();
        gate.SetResult();
        return await Task.WhenAll(attempts);
    }

    private async Task<Angebot> ReloadAsync(int angebotId)
    {
        await using var context = fixture.CreateContext();
        return await LoadAsync(context, angebotId);
    }

    /// <summary>
    /// What "consistent" means, recomputed independently of the stored columns: the stored totals
    /// must equal what the persisted lines add up to.
    /// </summary>
    private static void AssertStoredTotalsMatchTheLines(Angebot reloaded)
    {
        var lines = reloaded.Sections.SelectMany(s => s.Items).ToList();
        var expectedNet = Money.Sum(lines.Select(i => i.LineTotal));
        var expectedGross = expectedNet + Money.Sum(reloaded.VatBreakdown.Select(v => v.VatAmount));

        Assert.Equal(expectedNet, reloaded.NetTotal);
        Assert.Equal(expectedGross, reloaded.GrossTotal);
    }

    // ---- 1. Concurrent line edits cannot leave the totals inconsistent ------------------------

    /// <summary>
    /// <b>Two editors change two different lines at the same time.</b> Each recalculates the totals
    /// from its own view of the tree, so without a token both commit and the stored totals carry
    /// only one editor's view while both line changes land. Exactly one must win, and whatever is
    /// persisted must add up.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ConcurrentEditsOfDifferentLines_LeaveExactlyOneWinner_AndTotalsThatMatchTheLines(int run)
    {
        _ = run; // Repetition is the point (CLAUDE.md §14); the value only names the run.
        var angebotId = await SeedAngebotWithTwoLinesAsync();

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        ChangeQuantity(await LoadAsync(first, angebotId), lineIndex: 0, quantity: 7m);
        ChangeQuantity(await LoadAsync(second, angebotId), lineIndex: 1, quantity: 9m);

        var outcomes = await RaceCommitsAsync(first, second);

        Assert.Single(outcomes, o => o is null);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o is not null));
        AssertStoredTotalsMatchTheLines(await ReloadAsync(angebotId));
    }

    // ---- 2. Edit versus approve --------------------------------------------------------------

    /// <summary>
    /// <b>An edit staged while the Angebot was a Draft must not land after it was approved.</b> The
    /// stale editor never writes <c>Status</c>, so the edit lock it passed in memory is the only thing
    /// standing between BR-1 and a changed approved document — and the lock only saw the state its own
    /// context loaded.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AnEditStagedOnADraft_CannotLandAfterTheAngebotWasApproved(int run)
    {
        _ = run;
        var angebotId = await SeedAngebotWithTwoLinesAsync();

        await using var staleEditor = fixture.CreateContext();
        ChangeQuantity(await LoadAsync(staleEditor, angebotId), lineIndex: 0, quantity: 99m);

        await using (var reviewer = fixture.CreateContext())
        {
            var angebot = await LoadAsync(reviewer, angebotId);
            angebot.SubmitForReview();
            angebot.Approve(reviewedByAdminId: angebot.CreatedByInspectorId);
            await new UnitOfWork(reviewer).SaveChangesAsync(CancellationToken.None);
        }

        var outcome = await CommitAsync(staleEditor, Task.CompletedTask);

        Assert.IsType<ConflictException>(outcome);
        var reloaded = await ReloadAsync(angebotId);
        Assert.Equal(AngebotStatus.ApprovedInternally, reloaded.Status);
        Assert.Equal(OriginalQuantity, Line(reloaded, 0).Item.Quantity);
        AssertStoredTotalsMatchTheLines(reloaded);
    }

    // ---- 3. A description-only edit participates ---------------------------------------------

    /// <summary>
    /// <b>An edit that moves no money writes no total</b> — <c>Money</c> is a record, so an unchanged
    /// total is not marked modified — and therefore wrote nothing to the <c>Angebote</c> row at all.
    /// Without a column every mutation writes, such an edit could never be refused, however stale.
    /// Here it was staged on a Draft and lands after the Angebot was submitted for review.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ADescriptionOnlyEditStagedOnADraft_CannotLandAfterTheAngebotWasSubmitted(int run)
    {
        _ = run;
        var angebotId = await SeedAngebotWithTwoLinesAsync();

        await using var staleEditor = fixture.CreateContext();
        ChangeDescription(await LoadAsync(staleEditor, angebotId), lineIndex: 0, description: "Tippfehler korrigiert");

        await using (var author = fixture.CreateContext())
        {
            var angebot = await LoadAsync(author, angebotId);
            angebot.SubmitForReview();
            await new UnitOfWork(author).SaveChangesAsync(CancellationToken.None);
        }

        var outcome = await CommitAsync(staleEditor, Task.CompletedTask);

        Assert.IsType<ConflictException>(outcome);
        var reloaded = await ReloadAsync(angebotId);
        Assert.Equal(AngebotStatus.InReview, reloaded.Status);
        Assert.Equal(OriginalDescription, Line(reloaded, 0).Item.Description);
    }

    /// <summary>
    /// The same participation in a direct race: two description-only edits of one line, committed at
    /// once. One wins; the other is refused rather than silently overwriting it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ConcurrentDescriptionOnlyEdits_LeaveExactlyOneWinner(int run)
    {
        _ = run;
        var angebotId = await SeedAngebotWithTwoLinesAsync();

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        ChangeDescription(await LoadAsync(first, angebotId), lineIndex: 0, description: "Version A");
        ChangeDescription(await LoadAsync(second, angebotId), lineIndex: 0, description: "Version B");

        var outcomes = await RaceCommitsAsync(first, second);

        Assert.Single(outcomes, o => o is null);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o is not null));

        var winner = outcomes[0] is null ? "Version A" : "Version B";
        Assert.Equal(winner, Line(await ReloadAsync(angebotId), 0).Item.Description);
    }
}
