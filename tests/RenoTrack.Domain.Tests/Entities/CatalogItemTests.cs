using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Tests.Entities;

public class CatalogItemTests
{
    // ---- Create -------------------------------------------------------

    [Fact]
    public void Create_SetsAllFields()
    {
        var item = CatalogItem.Create(
            title: "Bodenbelag trockengepresste Fliesen/Platten",
            defaultUnit: ItemUnit.SquareMeter(),
            suggestedUnitPrice: Money.FromExact(82.25m),
            defaultSpecification: "Feinsteinzeug, rektifiziert",
            createdFromAngebotItemId: 17);

        Assert.Equal("Bodenbelag trockengepresste Fliesen/Platten", item.Title);
        Assert.Equal(ItemUnit.SquareMeter(), item.DefaultUnit);
        Assert.Equal(Money.FromExact(82.25m), item.SuggestedUnitPrice);
        Assert.Equal("Feinsteinzeug, rektifiziert", item.DefaultSpecification);
        Assert.Equal(17, item.CreatedFromAngebotItemId);
    }

    [Fact]
    public void Create_StartsNotRetired()
    {
        var item = CatalogItem.Create("Grundierung", ItemUnit.SquareMeter(), Money.FromExact(4.54m));

        Assert.False(item.IsRetired);
    }

    [Fact]
    public void Create_AllowsOmittingSpecificationAndCreatedFromAngebotItemId()
    {
        var item = CatalogItem.Create("Grundierung", ItemUnit.SquareMeter(), Money.FromExact(4.54m));

        Assert.Null(item.DefaultSpecification);
        Assert.Null(item.CreatedFromAngebotItemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsEmptyTitle(string emptyTitle)
    {
        Assert.Throws<ArgumentException>(() =>
            CatalogItem.Create(emptyTitle, ItemUnit.SquareMeter(), Money.FromExact(10.00m)));
    }

    [Fact]
    public void Create_RejectsNegativeSuggestedUnitPrice()
    {
        Assert.Throws<ArgumentException>(() =>
            CatalogItem.Create("Some item", ItemUnit.SquareMeter(), Money.FromExact(-1.00m)));
    }

    // ---- Update ---------------------------------------------------------

    [Fact]
    public void Update_ChangesEditableFields()
    {
        var item = CatalogItem.Create("Old title", ItemUnit.SquareMeter(), Money.FromExact(10.00m), "Old spec");

        item.Update("New title", ItemUnit.Piece(), Money.FromExact(20.00m), "New spec");

        Assert.Equal("New title", item.Title);
        Assert.Equal(ItemUnit.Piece(), item.DefaultUnit);
        Assert.Equal(Money.FromExact(20.00m), item.SuggestedUnitPrice);
        Assert.Equal("New spec", item.DefaultSpecification);
    }

    [Fact]
    public void Update_DoesNotChangeCreatedFromAngebotItemIdOrCreatedAt()
    {
        var item = CatalogItem.Create("Title", ItemUnit.SquareMeter(), Money.FromExact(10.00m), createdFromAngebotItemId: 5);
        var createdAtBefore = item.CreatedAt;

        item.Update("New title", ItemUnit.Piece(), Money.FromExact(20.00m));

        Assert.Equal(5, item.CreatedFromAngebotItemId);
        Assert.Equal(createdAtBefore, item.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_RejectsEmptyTitle(string emptyTitle)
    {
        var item = CatalogItem.Create("Title", ItemUnit.SquareMeter(), Money.FromExact(10.00m));

        Assert.Throws<ArgumentException>(() => item.Update(emptyTitle, ItemUnit.SquareMeter(), Money.FromExact(10.00m)));
    }

    [Fact]
    public void Update_RejectsNegativeSuggestedUnitPrice()
    {
        var item = CatalogItem.Create("Title", ItemUnit.SquareMeter(), Money.FromExact(10.00m));

        Assert.Throws<ArgumentException>(() => item.Update("Title", ItemUnit.SquareMeter(), Money.FromExact(-5.00m)));
    }

    // ---- Retire (BR-12) -------------------------------------------------

    [Fact]
    public void Retire_SetsIsRetired()
    {
        var item = CatalogItem.Create("Title", ItemUnit.SquareMeter(), Money.FromExact(10.00m));

        item.Retire();

        Assert.True(item.IsRetired);
    }

    [Fact]
    public void Retire_IsIdempotent()
    {
        var item = CatalogItem.Create("Title", ItemUnit.SquareMeter(), Money.FromExact(10.00m));
        item.Retire();

        var exception = Record.Exception(item.Retire);

        Assert.Null(exception);
        Assert.True(item.IsRetired);
    }

    // ---- BR-8 isolation proof --------------------------------------------

    [Fact]
    public void UpdatingACatalogItem_DoesNotAffectAnAngebotItemAlreadyCreatedFromIt_BR8()
    {
        var catalogItem = CatalogItem.Create(
            "Bodenbelag trockengepresste Fliesen/Platten",
            ItemUnit.SquareMeter(),
            Money.FromExact(82.25m),
            "Feinsteinzeug, rektifiziert");

        // Simulates what AddAngebotItemCommand does per BR-8: copy the Catalog item's values
        // into a new AngebotItem at creation time, rather than referencing CatalogItem live.
        var angebotItem = new AngebotItem(
            description: catalogItem.Title,
            quantity: 10m,
            unit: catalogItem.DefaultUnit,
            unitPrice: catalogItem.SuggestedUnitPrice,
            vatRate: VatRate.Standard,
            specification: catalogItem.DefaultSpecification,
            catalogItemId: catalogItem.Id);

        // The Catalog item is edited afterward — a completely different title, unit, and price.
        catalogItem.Update("Renamed and repriced", ItemUnit.Piece(), Money.FromExact(999.99m), "Different spec");
        catalogItem.Retire();

        Assert.Equal("Bodenbelag trockengepresste Fliesen/Platten", angebotItem.Description);
        Assert.Equal(ItemUnit.SquareMeter(), angebotItem.Unit);
        Assert.Equal(Money.FromExact(82.25m), angebotItem.UnitPrice);
        Assert.Equal("Feinsteinzeug, rektifiziert", angebotItem.Specification);
    }
}
