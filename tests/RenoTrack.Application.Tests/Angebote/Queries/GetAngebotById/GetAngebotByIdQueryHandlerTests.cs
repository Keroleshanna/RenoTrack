using RenoTrack.Application.Angebote.Queries.GetAngebotById;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Queries.GetAngebotById;

public class GetAngebotByIdQueryHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeCatalogItemQueries _catalogItemQueries = new();
    private readonly GetAngebotByIdQueryHandler _handler;

    public GetAngebotByIdQueryHandlerTests()
    {
        _handler = new GetAngebotByIdQueryHandler(
            _angebotRepository, _catalogItemQueries, new OwnershipValidator());
    }

    /// <summary>Two sections with mixed VAT rates, so the breakdown has something to prove.</summary>
    private Angebot SeedAngebot()
    {
        var angebot = _angebotRepository.Seed(
            Angebot.Create(leadId: 1, inspectionId: 7, "ANG-2026-00001", OwningInspectorId));

        var second = angebot.AddSection("Pos. 2", 2);
        var first = angebot.AddSection("Pos. 1", 1);

        angebot.AddItemToSection(first, "Standard rate", 2m, ItemUnit.Piece(), Money.FromExact(100.00m), VatRate.Standard);
        angebot.AddItemToSection(second, "Zero rate", 1m, ItemUnit.Piece(), Money.FromExact(50.00m), VatRate.Zero);

        angebot.AssignChildIds();
        return angebot;
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheWholeTreeWithTotals()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(angebot.Id, result.Id);
        Assert.Equal("ANG-2026-00001", result.AngebotNumber);
        Assert.Equal(AngebotStatus.Draft, result.Status);
        Assert.Equal(250.00m, result.NetTotal);
        Assert.Equal(288.00m, result.GrossTotal); // 200 @ 19% => 38.00 VAT; 50 @ 0% => none
        Assert.Equal(2, result.Sections.Count);
    }

    /// <summary>
    /// Sections come back in the document's own order, not insertion order — the seed adds
    /// "Pos. 2" first precisely so an unsorted mapping would fail here.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OrdersSectionsBySortOrder()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(["Pos. 1", "Pos. 2"], result.Sections.Select(s => s.Title));
    }

    [Fact]
    public async Task HandleAsync_ReturnsOneVatBreakdownLinePerDistinctRate()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, OwningInspectorId), CancellationToken.None);

        Assert.Equal(2, result.VatBreakdown.Count);

        var standard = Assert.Single(result.VatBreakdown, line => line.Rate == VatRate.Standard);
        Assert.Equal(200.00m, standard.NetAmount);
        Assert.Equal(38.00m, standard.VatAmount);

        var zero = Assert.Single(result.VatBreakdown, line => line.Rate == VatRate.Zero);
        Assert.Equal(50.00m, zero.NetAmount);
        Assert.Equal(0m, zero.VatAmount);
    }

    [Fact]
    public async Task HandleAsync_IncludesItemsWithinTheirSections()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, OwningInspectorId), CancellationToken.None);

        var first = result.Sections[0];
        var item = Assert.Single(first.Items);

        Assert.Equal("Standard rate", item.Description);
        Assert.Equal(200.00m, item.LineTotal);
        Assert.Equal(200.00m, first.Subtotal);
    }

    /// <summary>
    /// A null RequestingInspectorId is an Admin, who is "F" for viewing any Angebot
    /// (PermissionMatrix.md §4) — so no ownership rule applies and none is enforced.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AdminSeesAnAngebotTheyDoNotOwn()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, RequestingInspectorId: null), CancellationToken.None);

        Assert.Equal(angebot.Id, result.Id);
    }

    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbidden()
    {
        var angebot = SeedAngebot();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new GetAngebotByIdQuery(angebot.Id, OtherInspectorId), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownAngebot_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new GetAngebotByIdQuery(999, OwningInspectorId), CancellationToken.None));
    }
}
