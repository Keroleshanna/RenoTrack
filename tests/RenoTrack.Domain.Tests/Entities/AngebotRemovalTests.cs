using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

/// <summary>
/// Removal of sections and items, added in Phase 5 on the strength of PermissionMatrix.md §3's
/// "Add/<b>remove</b> Sections &amp; Items — Inspector S" — the documented evidence CLAUDE.md §2
/// requires before a child remover may exist at all.
/// </summary>
/// <remarks>
/// A separate file from <see cref="AngebotTests"/> purely for size: that class already covers
/// creation, the section/item tree, totals, and the full transition matrix.
/// </remarks>
public class AngebotRemovalTests
{
    private const string ValidNumber = "ANG-2026-00001";

    private static Angebot DraftWithOneItem(out AngebotSection section, out AngebotItem item)
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        section = angebot.AddSection("Demolition", 1);
        item = angebot.AddItemToSection(section, "Strip walls", 2m, ItemUnit.Piece(), Money.FromExact(50.00m), VatRate.Standard);
        return angebot;
    }

    // ---- RemoveSection ------------------------------------------------

    [Fact]
    public void RemoveSection_RemovesItAndRecalculatesTotals()
    {
        var angebot = DraftWithOneItem(out var section, out _);
        Assert.Equal(Money.FromExact(100.00m), angebot.NetTotal);

        angebot.RemoveSection(section);

        Assert.Empty(angebot.Sections);
        Assert.Equal(Money.Zero, angebot.NetTotal);
        Assert.Equal(Money.Zero, angebot.GrossTotal);
    }

    [Fact]
    public void RemoveSection_LeavesOtherSectionsAndTheirTotalsIntact()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var kept = angebot.AddSection("Kept", 1);
        var removed = angebot.AddSection("Removed", 2);
        angebot.AddItemToSection(kept, "Keep", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);
        angebot.AddItemToSection(removed, "Drop", 1m, ItemUnit.Piece(), Money.FromExact(90.00m), VatRate.Standard);

        angebot.RemoveSection(removed);

        Assert.Equal(Money.FromExact(10.00m), angebot.NetTotal);
        Assert.Single(angebot.Sections);
        Assert.Equal("Kept", angebot.Sections[0].Title);
    }

    [Fact]
    public void RemoveSection_FromAnotherAngebot_Throws()
    {
        var angebot1 = Angebot.Create(1, 5, ValidNumber, 9);
        var angebot2 = Angebot.Create(2, 6, "ANG-2026-00002", 9);
        var foreignSection = angebot2.AddSection("Not mine", 1);

        Assert.Throws<InvalidOperationException>(() => angebot1.RemoveSection(foreignSection));
    }

    // ---- RemoveItem ---------------------------------------------------

    [Fact]
    public void RemoveItem_RemovesItAndRecalculatesTotals_LeavingTheSection()
    {
        var angebot = DraftWithOneItem(out var section, out var item);

        angebot.RemoveItem(section, item);

        Assert.Single(angebot.Sections);
        Assert.Empty(section.Items);
        Assert.Equal(Money.Zero, angebot.NetTotal);
    }

    [Fact]
    public void RemoveItem_FromASectionThatDoesNotHoldIt_Throws()
    {
        var angebot = Angebot.Create(1, 5, ValidNumber, 9);
        var sectionA = angebot.AddSection("A", 1);
        var sectionB = angebot.AddSection("B", 2);
        var itemInA = angebot.AddItemToSection(sectionA, "Item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Throws<InvalidOperationException>(() => angebot.RemoveItem(sectionB, itemInA));

        // And the failed attempt changed nothing.
        Assert.Single(sectionA.Items);
        Assert.Equal(Money.FromExact(10.00m), angebot.NetTotal);
    }

    [Fact]
    public void RemoveItem_WithASectionFromAnotherAngebot_Throws()
    {
        var angebot1 = DraftWithOneItem(out _, out var item);
        var angebot2 = Angebot.Create(2, 6, "ANG-2026-00002", 9);
        var foreignSection = angebot2.AddSection("Not mine", 1);

        Assert.Throws<InvalidOperationException>(() => angebot1.RemoveItem(foreignSection, item));
    }

    // ---- Edit lock (StateMachine.md §2.4) ------------------------------

    [Fact]
    public void RemoveSection_WhileInReview_Throws()
    {
        var angebot = DraftWithOneItem(out var section, out _);
        angebot.SubmitForReview();

        var exception = Assert.Throws<InvalidOperationException>(() => angebot.RemoveSection(section));

        Assert.Contains(nameof(AngebotStatus.InReview), exception.Message, StringComparison.Ordinal);
        Assert.Single(angebot.Sections);
    }

    [Fact]
    public void RemoveItem_WhileInReview_Throws()
    {
        var angebot = DraftWithOneItem(out var section, out var item);
        angebot.SubmitForReview();

        Assert.Throws<InvalidOperationException>(() => angebot.RemoveItem(section, item));
        Assert.Single(section.Items);
    }

    [Fact]
    public void RemoveSection_WhileApprovedInternally_Throws()
    {
        var angebot = DraftWithOneItem(out var section, out _);
        angebot.SubmitForReview();
        angebot.Approve(reviewedByAdminId: 3);

        Assert.Throws<InvalidOperationException>(() => angebot.RemoveSection(section));
    }

    /// <summary>
    /// Removal counts as "editing resumes" for StateMachine.md §2.3's implicit
    /// <c>ChangesRequested → Draft</c> transition, exactly as adding does — the aggregate reaches it
    /// through the same <c>EnsureEditable</c> gate, so this is behaviour, not coincidence.
    /// </summary>
    [Fact]
    public void RemoveItem_WhileChangesRequested_ReopensTheAngebotAsDraft()
    {
        var angebot = DraftWithOneItem(out var section, out var item);
        angebot.SubmitForReview();
        angebot.RequestChanges(reviewedByAdminId: 3);
        Assert.Equal(AngebotStatus.ChangesRequested, angebot.Status);

        angebot.RemoveItem(section, item);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
        Assert.Empty(section.Items);
    }

    [Fact]
    public void RemoveSection_WhileChangesRequested_ReopensTheAngebotAsDraft()
    {
        var angebot = DraftWithOneItem(out var section, out _);
        angebot.SubmitForReview();
        angebot.RequestChanges(reviewedByAdminId: 3);

        angebot.RemoveSection(section);

        Assert.Equal(AngebotStatus.Draft, angebot.Status);
        Assert.Empty(angebot.Sections);
    }

    // ---- Aggregate boundary --------------------------------------------

    /// <summary>
    /// <c>AngebotSection.RemoveItem</c> must stay unreachable from outside the Domain, like
    /// <c>AddItem</c> — a public remover there would let a caller shrink an Angebot without the
    /// edit-lock check or the totals recalculation that <see cref="Angebot.RemoveItem"/> performs.
    /// </summary>
    [Fact]
    public void AngebotSection_RemoveItem_IsNotPubliclyReachable()
    {
        var method = typeof(AngebotSection).GetMethod(
            nameof(Angebot.RemoveItem),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.Null(method);
    }
}
