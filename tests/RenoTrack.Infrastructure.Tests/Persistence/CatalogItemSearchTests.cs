using RenoTrack.Application.Common;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The search term and paging <c>CatalogItemQueries.SearchAsync</c> gained in Phase 5, proven
/// against real LocalDB — in particular that <c>EF.Functions.Like</c> matches case-insensitively
/// through the column's own collation, which is the reason it was chosen over
/// <c>string.Contains</c>.
/// </summary>
/// <remarks>
/// Every item here carries a run-unique marker in its title, because this class shares one database
/// with the rest of the collection and rows seeded elsewhere would otherwise leak into a count.
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class CatalogItemSearchTests(RenoTrackDbContextFixture fixture)
{
    private readonly string _marker = $"MK{Guid.NewGuid():N}"[..10];

    private async Task SeedAsync(params (string Title, string? Specification)[] items)
    {
        await using var context = fixture.CreateContext();

        foreach (var (title, specification) in items)
        {
            context.CatalogItems.Add(CatalogItem.Create(
                $"{title} {_marker}", ItemUnit.SquareMeter(), Money.FromExact(10.00m), specification));
        }

        await context.SaveChangesAsync();
    }

    private async Task<PagedResult<Application.CatalogItems.Dtos.CatalogItemDto>> SearchAsync(
        string? term, int page = Pagination.FirstPage, int pageSize = Pagination.DefaultPageSize)
    {
        await using var context = fixture.CreateContext();
        return await new CatalogItemQueries(context).SearchAsync(term, page, pageSize, CancellationToken.None);
    }

    /// <summary>
    /// Case-insensitivity is asserted on the run-unique marker, which is part of every seeded title:
    /// searching a real word like "fliesen" would also match rows other test classes seeded into
    /// this shared database, making the count meaningless.
    /// </summary>
    [Fact]
    public async Task SearchAsync_MatchesTitleCaseInsensitively()
    {
        await SeedAsync(("Fliesen verlegen", null), ("Malerarbeiten", null));

        var lower = await SearchAsync(_marker.ToLowerInvariant());
        var upper = await SearchAsync(_marker.ToUpperInvariant());

        Assert.Equal(2, lower.TotalCount);
        Assert.Equal(2, upper.TotalCount);
        Assert.Contains(lower.Items, i => i.Title.StartsWith("Fliesen verlegen", StringComparison.Ordinal));
    }

    /// <summary>A term matching a mid-title word still matches — the predicate is a contains, not a prefix.</summary>
    [Fact]
    public async Task SearchAsync_MatchesAWordInTheMiddleOfATitle()
    {
        await SeedAsync(("Fliesen verlegen", null));

        var result = await SearchAsync("verlegen");

        Assert.Contains(result.Items, i => i.Title.EndsWith(_marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_MatchesSpecificationToo()
    {
        await SeedAsync(("Grundierung", "Feinsteinzeug"), ("Malerarbeiten", null));

        var result = await SearchAsync("feinsteinzeug");

        Assert.Contains(result.Items, i => i.Title.StartsWith("Grundierung", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_WithNoTermReturnsEverythingNotRetired()
    {
        await SeedAsync(("Alpha", null), ("Beta", null));

        var result = await SearchAsync(_marker);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_PagesWithATotalCountThatIgnoresThePage()
    {
        await SeedAsync(("Alpha", null), ("Beta", null), ("Gamma", null));

        var firstPage = await SearchAsync(_marker, page: 1, pageSize: 2);
        var secondPage = await SearchAsync(_marker, page: 2, pageSize: 2);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(3, secondPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage.Items);

        // Ordered by title, so the pages partition the set rather than overlapping.
        Assert.Empty(firstPage.Items.Select(i => i.Id).Intersect(secondPage.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task SearchAsync_ExcludesRetiredItemsEvenWhenTheyMatchTheTerm()
    {
        await using (var context = fixture.CreateContext())
        {
            var retired = CatalogItem.Create(
                $"Retired {_marker}", ItemUnit.Piece(), Money.FromExact(1.00m));
            retired.Retire();
            context.CatalogItems.Add(retired);
            await context.SaveChangesAsync();
        }

        var result = await SearchAsync(_marker);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAnEmptyPageWhenNothingMatches()
    {
        var result = await SearchAsync($"no-such-item-{_marker}");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}
