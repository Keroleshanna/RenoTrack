using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves CatalogItemQueries.SearchAsync's actual behavior against real LocalDB — including that
/// the DTO projection expression is genuinely translatable by EF Core (a real risk being tested,
/// not assumed), that retired items are excluded (BR-12), and that a retired item is still
/// reachable via CatalogItemRepository.GetByIdAsync (BR-14/D38, tested separately in
/// CatalogItemRepositoryTests — this class only asserts SearchAsync's own exclusion).
/// </summary>
[Collection("Infrastructure Database")]
public sealed class CatalogItemQueriesTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task SearchAsync_ReturnsAllFieldsCorrectlyProjected()
    {
        var catalogItem = CatalogItem.Create(
            "Fliesen verlegen", ItemUnit.SquareMeter(), Money.FromExact(82.25m), "Feinsteinzeug");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.CatalogItems.Add(catalogItem);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var queries = new CatalogItemQueries(readContext);

        var results = await queries.SearchAsync(CancellationToken.None);

        var dto = Assert.Single(results, r => r.Id == catalogItem.Id);
        Assert.Equal("Fliesen verlegen", dto.Title);
        Assert.Equal("Feinsteinzeug", dto.DefaultSpecification);
        Assert.Equal("m2", dto.DefaultUnit);
        Assert.Equal(82.25m, dto.SuggestedUnitPrice);
        Assert.Null(dto.CreatedFromAngebotItemId);
        Assert.False(dto.IsRetired);
    }

    [Fact]
    public async Task SearchAsync_ExcludesRetiredItems()
    {
        var active = CatalogItem.Create("Active Item", ItemUnit.Piece(), Money.FromExact(10.00m));
        var retired = CatalogItem.Create("Retired Item", ItemUnit.Piece(), Money.FromExact(20.00m));
        retired.Retire();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.CatalogItems.Add(active);
            writeContext.CatalogItems.Add(retired);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var queries = new CatalogItemQueries(readContext);

        var results = await queries.SearchAsync(CancellationToken.None);

        Assert.Contains(results, r => r.Id == active.Id);
        Assert.DoesNotContain(results, r => r.Id == retired.Id);
    }

    [Fact]
    public async Task SearchAsync_AfterAddingANewCatalogItem_IncludesItInTheResultCount()
    {
        // This class shares the "Infrastructure Database" collection with every other test
        // class, so CatalogItems may already contain rows seeded elsewhere in this run — assert
        // on this test's own item appearing and the count increasing by exactly one, not on
        // total emptiness beforehand.
        await using var context = fixture.CreateContext();
        var queries = new CatalogItemQueries(context);
        var beforeCount = (await queries.SearchAsync(CancellationToken.None)).Count;

        var item = CatalogItem.Create("Isolation Check Item", ItemUnit.Piece(), Money.FromExact(1.00m));
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();

        var afterResults = await queries.SearchAsync(CancellationToken.None);

        Assert.Equal(beforeCount + 1, afterResults.Count);
        Assert.Contains(afterResults, r => r.Id == item.Id);
    }
}
