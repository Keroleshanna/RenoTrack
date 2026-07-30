using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;
using RenoTrack.Application.Tests.Fakes;

namespace RenoTrack.Application.Tests.CatalogItems.Queries.SearchCatalogItems;

public class SearchCatalogItemsQueryHandlerTests
{
    private readonly FakeCatalogItemQueries _catalogItemQueries = new();
    private readonly SearchCatalogItemsQueryHandler _handler;

    public SearchCatalogItemsQueryHandlerTests()
    {
        _handler = new SearchCatalogItemsQueryHandler(_catalogItemQueries);
    }

    private static CatalogItemDto MakeDto(int id, string title, bool isRetired) =>
        new(id, title, null, "m2", 10.00m, null, isRetired, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_ReturnsAllActiveCatalogItems()
    {
        _catalogItemQueries.Seed(MakeDto(1, "Fliesen verlegen", isRetired: false));
        _catalogItemQueries.Seed(MakeDto(2, "Grundierung", isRetired: false));

        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Title == "Fliesen verlegen");
        Assert.Contains(result, i => i.Title == "Grundierung");
    }

    // ---- BR-12: retired items excluded -------------------------------------

    [Fact]
    public async Task HandleAsync_ExcludesRetiredCatalogItems()
    {
        _catalogItemQueries.Seed(MakeDto(1, "Active item", isRetired: false));
        _catalogItemQueries.Seed(MakeDto(2, "Retired item", isRetired: true));

        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("Active item", item.Title);
    }

    [Fact]
    public async Task HandleAsync_NoCatalogItemsExist_ReturnsEmptyList()
    {
        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
