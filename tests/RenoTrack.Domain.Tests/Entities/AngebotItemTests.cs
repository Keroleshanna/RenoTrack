using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class AngebotItemTests
{
    // ---- Construction ---------------------------------------------------

    [Fact]
    public void Create_WithValidValues_SetsAllFields()
    {
        var item = new AngebotItem(
            description: "Bodenbelag trockengepresste Fliesen/Platten",
            quantity: 13.77m,
            unit: ItemUnit.SquareMeter(),
            unitPrice: Money.FromExact(18.56m),
            vatRate: VatRate.Standard,
            specification: "Feinsteinzeug, rektifiziert, 60x60cm",
            catalogItemId: 42);

        Assert.Equal("Bodenbelag trockengepresste Fliesen/Platten", item.Description);
        Assert.Equal("Feinsteinzeug, rektifiziert, 60x60cm", item.Specification);
        Assert.Equal(13.77m, item.Quantity);
        Assert.Equal(ItemUnit.SquareMeter(), item.Unit);
        Assert.Equal(Money.FromExact(18.56m), item.UnitPrice);
        Assert.Equal(VatRate.Standard, item.VatRate);
        Assert.Equal(42, item.CatalogItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5.5)]
    public void Create_RejectsQuantityLessThanOrEqualToZero(double invalidQuantity)
    {
        Assert.Throws<ArgumentException>(() => new AngebotItem(
            "Some item", (decimal)invalidQuantity, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard));
    }

    [Fact]
    public void Create_RejectsNegativeUnitPrice()
    {
        Assert.Throws<ArgumentException>(() => new AngebotItem(
            "Some item", 1m, ItemUnit.Piece(), Money.FromExact(-5.00m), VatRate.Standard));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyDescription(string emptyDescription)
    {
        Assert.Throws<ArgumentException>(() => new AngebotItem(
            emptyDescription, 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard));
    }

    [Fact]
    public void Specification_IsOptional()
    {
        var item = new AngebotItem("Some item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Null(item.Specification);
    }

    [Fact]
    public void CatalogItemId_IsOptional()
    {
        var item = new AngebotItem("Some item", 1m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Null(item.CatalogItemId);
    }

    // ---- LineTotal --------------------------------------------------------

    [Fact]
    public void LineTotal_MultipliesQuantityByUnitPrice()
    {
        var item = new AngebotItem("Some item", 2m, ItemUnit.Piece(), Money.FromExact(10.00m), VatRate.Standard);

        Assert.Equal(Money.FromExact(20.00m), item.LineTotal);
    }

    [Fact]
    public void LineTotal_HandlesDecimalQuantity_RealisticExample()
    {
        // From Sequence Diagram §4's own sample values: 13.77 m2 x 18.56 = 255.5712 (raw)
        var item = new AngebotItem(
            "Bodenbelag", 13.77m, ItemUnit.SquareMeter(), Money.FromExact(18.56m), VatRate.Standard);

        Assert.Equal(Money.FromExact(255.57m), item.LineTotal);
    }

    [Fact]
    public void LineTotal_AppliesBR11MidpointRoundingAwayFromZero_NotBypassed()
    {
        // 0.5 x 1.01 = 0.505 exactly — a genuine midpoint at the 3rd decimal.
        // If LineTotal used plain truncation or ToEven instead of BR-11's AwayFromZero,
        // this would come out as 0.50, not 0.51 — proving the rounding is actually applied
        // through LineTotal itself, not bypassed.
        var item = new AngebotItem("Some item", 0.5m, ItemUnit.LumpSum(), Money.FromExact(1.01m), VatRate.Standard);

        Assert.Equal(Money.FromExact(0.51m), item.LineTotal);
    }
}
