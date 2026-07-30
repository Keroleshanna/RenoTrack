using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class CatalogItemPersistenceTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task AddingACatalogItem_PersistsAndReloadsAllFields()
    {
        var catalogItem = CatalogItem.Create("Fliesen verlegen", ItemUnit.SquareMeter(), Money.FromExact(45.50m), "Feinsteinzeug, rutschfest");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.CatalogItems.Add(catalogItem);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.CatalogItems.SingleAsync(c => c.Id == catalogItem.Id);

        Assert.Equal("Fliesen verlegen", reloaded.Title);
        Assert.Equal("Feinsteinzeug, rutschfest", reloaded.DefaultSpecification);
        Assert.Equal(ItemUnit.SquareMeter(), reloaded.DefaultUnit);
        Assert.Equal(Money.FromExact(45.50m), reloaded.SuggestedUnitPrice);
        Assert.False(reloaded.IsRetired);
        Assert.Null(reloaded.CreatedFromAngebotItemId);
    }

    [Fact]
    public async Task RetiringACatalogItem_PersistsTheRetiredFlag()
    {
        var catalogItem = CatalogItem.Create("Grundierung", ItemUnit.SquareMeter(), Money.FromExact(4.54m));

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.CatalogItems.Add(catalogItem);
            await writeContext.SaveChangesAsync();
        }

        await using (var updateContext = fixture.CreateContext())
        {
            var toRetire = await updateContext.CatalogItems.SingleAsync(c => c.Id == catalogItem.Id);
            toRetire.Retire();
            await updateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.CatalogItems.SingleAsync(c => c.Id == catalogItem.Id);

        Assert.True(reloaded.IsRetired);
    }
}
