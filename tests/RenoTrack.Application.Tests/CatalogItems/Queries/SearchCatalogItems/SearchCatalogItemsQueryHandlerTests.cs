using FluentValidation;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.CatalogItems.Queries.SearchCatalogItems;
using RenoTrack.Application.Common;
using RenoTrack.Application.Tests.Fakes;

namespace RenoTrack.Application.Tests.CatalogItems.Queries.SearchCatalogItems;

public class SearchCatalogItemsQueryHandlerTests
{
    private readonly FakeCatalogItemQueries _catalogItemQueries = new();
    private readonly SearchCatalogItemsQueryHandler _handler;

    public SearchCatalogItemsQueryHandlerTests()
    {
        _handler = new SearchCatalogItemsQueryHandler(
            new SearchCatalogItemsQueryValidator(), _catalogItemQueries);
    }

    private static CatalogItemDto MakeDto(int id, string title, bool isRetired, string? specification = null) =>
        new(id, title, specification, "m2", 10.00m, null, isRetired, DateTime.UtcNow);

    [Fact]
    public async Task HandleAsync_ReturnsAllActiveCatalogItems()
    {
        _catalogItemQueries.Seed(MakeDto(1, "Fliesen verlegen", isRetired: false));
        _catalogItemQueries.Seed(MakeDto(2, "Grundierung", isRetired: false));

        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.Title == "Fliesen verlegen");
        Assert.Contains(result.Items, i => i.Title == "Grundierung");
    }

    // ---- BR-12: retired items excluded -------------------------------------

    [Fact]
    public async Task HandleAsync_ExcludesRetiredCatalogItems()
    {
        _catalogItemQueries.Seed(MakeDto(1, "Active item", isRetired: false));
        _catalogItemQueries.Seed(MakeDto(2, "Retired item", isRetired: true));

        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Active item", item.Title);
    }

    [Fact]
    public async Task HandleAsync_NoCatalogItemsExist_ReturnsEmptyPage()
    {
        var result = await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    // ---- Search (Wireframe D2's "Search Catalog" box) -----------------------

    [Fact]
    public async Task HandleAsync_ForwardsTheSearchTermAndPaging()
    {
        await _handler.HandleAsync(
            new SearchCatalogItemsQuery("fliese", Page: 2, PageSize: 10), CancellationToken.None);

        var call = Assert.Single(_catalogItemQueries.Calls);
        Assert.Equal("fliese", call.SearchTerm);
        Assert.Equal(2, call.Page);
        Assert.Equal(10, call.PageSize);
    }

    [Fact]
    public async Task HandleAsync_DefaultsToTheFirstPageAndDefaultSize()
    {
        await _handler.HandleAsync(new SearchCatalogItemsQuery(), CancellationToken.None);

        var call = Assert.Single(_catalogItemQueries.Calls);
        Assert.Null(call.SearchTerm);
        Assert.Equal(Pagination.FirstPage, call.Page);
        Assert.Equal(Pagination.DefaultPageSize, call.PageSize);
    }

    [Fact]
    public async Task HandleAsync_MatchesOnTitleOrSpecification()
    {
        _catalogItemQueries.Seed(MakeDto(1, "Fliesen verlegen", isRetired: false));
        _catalogItemQueries.Seed(MakeDto(2, "Grundierung", isRetired: false, specification: "Feinsteinzeug"));
        _catalogItemQueries.Seed(MakeDto(3, "Malerarbeiten", isRetired: false));

        var byTitle = await _handler.HandleAsync(new SearchCatalogItemsQuery("fliesen"), CancellationToken.None);
        var bySpec = await _handler.HandleAsync(new SearchCatalogItemsQuery("feinstein"), CancellationToken.None);

        Assert.Equal("Fliesen verlegen", Assert.Single(byTitle.Items).Title);
        Assert.Equal("Grundierung", Assert.Single(bySpec.Items).Title);
    }

    [Fact]
    public async Task HandleAsync_TotalCountReflectsTheFilterNotThePage()
    {
        for (var i = 1; i <= 5; i++)
        {
            _catalogItemQueries.Seed(MakeDto(i, $"Fliesen {i}", isRetired: false));
        }

        var result = await _handler.HandleAsync(
            new SearchCatalogItemsQuery(Page: 1, PageSize: 2), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
    }

    // ---- Shape validation (CLAUDE.md §5) -----------------------------------

    [Theory]
    [InlineData(0, Pagination.DefaultPageSize)]
    [InlineData(Pagination.FirstPage, 0)]
    [InlineData(Pagination.FirstPage, Pagination.MaxPageSize + 1)]
    public async Task HandleAsync_InvalidPaging_ThrowsValidationException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new SearchCatalogItemsQuery(Page: page, PageSize: pageSize), CancellationToken.None));
    }
}
