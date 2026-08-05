using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;

/// <summary>
/// SRS FR-4.10. The behaviour worth pinning is what is copied and what deliberately is not, plus the
/// provenance link — and that no ownership rule applies, since PermissionMatrix.md §3 marks this "F".
/// </summary>
public class SaveAngebotItemAsCatalogItemCommandHandlerTests
{
    private const int AngebotItemId = 42;
    private const int InspectorId = 5;

    private readonly FakeAngebotQueries _angebotQueries = new();
    private readonly FakeCatalogItemRepository _catalogItemRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly SaveAngebotItemAsCatalogItemCommandHandler _handler;

    public SaveAngebotItemAsCatalogItemCommandHandlerTests()
    {
        _handler = new SaveAngebotItemAsCatalogItemCommandHandler(
            new SaveAngebotItemAsCatalogItemCommandValidator(),
            _angebotQueries,
            _catalogItemRepository,
            _unitOfWork,
            _auditService);
    }

    private void SeedItem(string? specification = "Feinsteinzeug", int? catalogItemId = null) =>
        _angebotQueries.Items[AngebotItemId] = new ItemDto(
            AngebotItemId,
            catalogItemId,
            "Fliesen verlegen",
            specification,
            Quantity: 13.5m,
            Unit: "m2",
            UnitPrice: 82.25m,
            VatRate: VatRate.Standard,
            LineTotal: 1110.38m);

    [Fact]
    public async Task HandleAsync_CopiesTheItemsReusableValuesIntoANewCatalogItem()
    {
        SeedItem();

        var result = await _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(AngebotItemId, InspectorId), CancellationToken.None);

        Assert.Equal("Fliesen verlegen", result.Title);
        Assert.Equal("Feinsteinzeug", result.DefaultSpecification);
        Assert.Equal("m2", result.DefaultUnit);
        Assert.Equal(82.25m, result.SuggestedUnitPrice);
        Assert.False(result.IsRetired);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// The provenance link ERD.md documents — how the Catalog grew, and the only thing that
    /// distinguishes this path from an Admin's direct curation.
    /// </summary>
    [Fact]
    public async Task HandleAsync_RecordsWhichAngebotItemItCameFrom()
    {
        SeedItem();

        var result = await _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(AngebotItemId, InspectorId), CancellationToken.None);

        Assert.Equal(AngebotItemId, result.CreatedFromAngebotItemId);
        Assert.Equal(AngebotItemId, Assert.Single(_catalogItemRepository.AddedCatalogItems).CreatedFromAngebotItemId);
    }

    [Fact]
    public async Task HandleAsync_CopiesAnItemWithNoSpecification()
    {
        SeedItem(specification: null);

        var result = await _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(AngebotItemId, InspectorId), CancellationToken.None);

        Assert.Null(result.DefaultSpecification);
    }

    /// <summary>
    /// An item that already came <em>from</em> the Catalog can still be saved back as a new entry —
    /// nothing in FR-4.10 or BR-8 forbids it, and the Inspector may have adjusted the values. The
    /// new entry's provenance points at the item, not at the original Catalog entry.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AcceptsAnItemThatWasItselfCreatedFromTheCatalog()
    {
        SeedItem(catalogItemId: 7);

        var result = await _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(AngebotItemId, InspectorId), CancellationToken.None);

        Assert.Equal(AngebotItemId, result.CreatedFromAngebotItemId);
    }

    [Fact]
    public async Task HandleAsync_LogsTheCatalogItemCreatedMilestoneAgainstTheInspector()
    {
        SeedItem();

        await _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(AngebotItemId, InspectorId), CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(RenoTrack.Domain.Entities.CatalogItem), entry.EntityType);
        Assert.Equal(AuditAction.CatalogItemCreated, entry.Action);
        Assert.Equal(InspectorId, entry.PerformedByUserId);
    }

    [Fact]
    public async Task HandleAsync_UnknownItem_ThrowsNotFoundAndSavesNothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(999, InspectorId), CancellationToken.None));

        Assert.Empty(_catalogItemRepository.AddedCatalogItems);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(
            new SaveAngebotItemAsCatalogItemCommand(0, 0), CancellationToken.None));
    }
}
