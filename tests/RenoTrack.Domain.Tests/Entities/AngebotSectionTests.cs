using System.Reflection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class AngebotSectionTests
{
    // ---- Construction ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RequiresTitle(string emptyTitle)
    {
        Assert.Throws<ArgumentException>(() => new AngebotSection(emptyTitle, 1));
    }

    [Fact]
    public void Create_PreservesTitleAndSortOrder()
    {
        var section = new AngebotSection("Pos. 1 Baustelleneinrichtung", 1);

        Assert.Equal("Pos. 1 Baustelleneinrichtung", section.Title);
        Assert.Equal(1, section.SortOrder);
    }

    // ---- Subtotal -------------------------------------------------------

    [Fact]
    public void Subtotal_OfEmptySection_IsZero()
    {
        var section = new AngebotSection("Pos. 1", 1);

        Assert.Equal(Money.Zero, section.Subtotal);
    }

    [Fact]
    public void Subtotal_WithOneItem_EqualsThatItemsLineTotal()
    {
        var section = new AngebotSection("Pos. 1", 1);

        var item = section.AddItem("Some item", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Equal(item.LineTotal, section.Subtotal);
    }

    [Fact]
    public void Subtotal_WithMultipleItems_SumsAllLineTotals()
    {
        var section = new AngebotSection("Pos. 1", 1);
        section.AddItem("Item A", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard); // 20.00
        section.AddItem("Item B", 13.77m, ItemUnit.SquareMeter(), Money.FromExact(18.56m), VatRate.Standard); // 255.57

        Assert.Equal(Money.FromExact(275.57m), section.Subtotal);
    }

    // ---- Aggregate boundary ------------------------------------------------
    // Architecture.md §6.2: Angebot must be the only public entry point for adding items —
    // AngebotSection itself exposes no public way to be constructed or to accept an item.

    [Fact]
    public void HasNoPublicConstructor()
    {
        var publicConstructors = typeof(AngebotSection).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void AddItem_IsNotPubliclyAccessible()
    {
        var publicAddItem = typeof(AngebotSection).GetMethod("AddItem", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(publicAddItem);
    }
}
