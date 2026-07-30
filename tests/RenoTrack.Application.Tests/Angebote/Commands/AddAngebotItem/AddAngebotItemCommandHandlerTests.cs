using FluentValidation;
using RenoTrack.Application.Angebote.Commands.AddAngebotItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Commands.AddAngebotItem;

public class AddAngebotItemCommandHandlerTests
{
    private const int OwningInspectorId = 5;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeCatalogItemRepository _catalogItemRepository = new();
    private readonly OwnershipValidator _ownershipValidator = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AddAngebotItemCommandHandler _handler;

    public AddAngebotItemCommandHandlerTests()
    {
        _handler = new AddAngebotItemCommandHandler(
            new AddAngebotItemCommandValidator(),
            _angebotRepository,
            _catalogItemRepository,
            _ownershipValidator,
            _unitOfWork);
    }

    private (Angebot Angebot, AngebotSection Section) SeedDraftAngebotWithSection(int createdByInspectorId = OwningInspectorId)
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", createdByInspectorId));
        var section = angebot.AddSection("Pos. 1", 1);
        return (angebot, section);
    }

    /// <summary>
    /// Test-only seam simulating EF-assigned identity for a child section — CLAUDE.md §2 notes
    /// every freshly-created section shares Id == 0 pre-Phase 3; this is the first command that
    /// needs to target one of several sections by id, so this reflection assignment (test code
    /// only, mirroring the sanctioned Seed pattern for aggregate roots) is what makes that
    /// meaningfully testable.
    /// </summary>
    private static void AssignSectionId(AngebotSection section, int id) =>
        typeof(AngebotSection).GetProperty(nameof(AngebotSection.Id))!.SetValue(section, id);

    private CatalogItem SeedCatalogItem() =>
        _catalogItemRepository.Seed(CatalogItem.Create(
            "Bodenbelag trockengepresste Fliesen/Platten",
            ItemUnit.SquareMeter(),
            Money.FromExact(82.25m),
            "Feinsteinzeug, rektifiziert"));

    // ---- Happy path: custom item -------------------------------------------

    [Fact]
    public async Task HandleAsync_CustomItem_ReturnsItemDtoWithSubmittedValues()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, CatalogItemId: null,
            "Baustelleneinrichtung", "Gerüst und Absperrung", "pauschal",
            Quantity: 1m, UnitPrice: 450.00m, VatRate.Standard, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal("Baustelleneinrichtung", result.Item.Description);
        Assert.Equal("Gerüst und Absperrung", result.Item.Specification);
        Assert.Equal("pauschal", result.Item.Unit);
        Assert.Equal(450.00m, result.Item.UnitPrice);
        Assert.Equal(450.00m, result.Item.LineTotal);
        Assert.Null(result.Item.CatalogItemId);
    }

    [Fact]
    public async Task HandleAsync_ReturnsUpdatedAngebotSummary()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, null, "Item", null, "Stk", 2m, 100.00m, VatRate.Standard, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(angebot.Id, result.Summary.Id);
        Assert.Equal(angebot.AngebotNumber, result.Summary.AngebotNumber);
        Assert.Equal(angebot.NetTotal.Amount, result.Summary.NetTotal);
        Assert.Equal(angebot.GrossTotal.Amount, result.Summary.GrossTotal);
    }

    [Fact]
    public async Task HandleAsync_AddsItemToTheTargetSection()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, null, "Item", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Single(section.Items);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, null, "Item", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // ---- Happy path: Catalog-sourced item ----------------------------------

    [Fact]
    public async Task HandleAsync_CatalogSourcedItem_CopiesDescriptionSpecificationAndUnitFromCatalogItem()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var catalogItem = SeedCatalogItem();
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, catalogItem.Id, Description: null, Specification: null, UnitCode: null,
            Quantity: 13.77m, UnitPrice: 18.56m, VatRate.Standard, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(catalogItem.Title, result.Item.Description);
        Assert.Equal(catalogItem.DefaultSpecification, result.Item.Specification);
        Assert.Equal(catalogItem.DefaultUnit.Code, result.Item.Unit);
        Assert.Equal(catalogItem.Id, result.Item.CatalogItemId);
    }

    [Fact]
    public async Task HandleAsync_CatalogSourcedItem_UsesTheCommandsUnitPrice_NotTheCatalogItemsSuggestedPrice()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var catalogItem = SeedCatalogItem(); // SuggestedUnitPrice = 82.25
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, catalogItem.Id, null, null, null,
            Quantity: 1m, UnitPrice: 99.99m, VatRate.Standard, OwningInspectorId); // Inspector adjusted the price

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(99.99m, result.Item.UnitPrice);
        Assert.NotEqual(catalogItem.SuggestedUnitPrice.Amount, result.Item.UnitPrice);
    }

    // ---- BR-8/BR-14: snapshot independence through the Application layer ---

    [Fact]
    public async Task HandleAsync_CatalogSourcedItem_IsUnaffectedByLaterEditsToTheCatalogItem_BR8()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var catalogItem = SeedCatalogItem();
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, catalogItem.Id, null, null, null, 10m, 82.25m, VatRate.Standard, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        catalogItem.Update("Renamed and repriced", ItemUnit.Piece(), Money.FromExact(999.99m), "Different spec");
        catalogItem.Retire();

        Assert.Equal("Bodenbelag trockengepresste Fliesen/Platten", result.Item.Description);
        Assert.Equal("Feinsteinzeug, rektifiziert", result.Item.Specification);
        Assert.Equal("m2", result.Item.Unit);
        Assert.Equal(82.25m, result.Item.UnitPrice);
    }

    [Fact]
    public async Task HandleAsync_RetiredCatalogItem_IsStillAValidReference_BR14()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var catalogItem = SeedCatalogItem();
        catalogItem.Retire();
        var command = new AddAngebotItemCommand(
            angebot.Id, section.Id, catalogItem.Id, null, null, null, 1m, 82.25m, VatRate.Standard, OwningInspectorId);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(catalogItem.Title, result.Item.Description);
    }

    // ---- Multi-section targeting -------------------------------------------

    [Fact]
    public async Task HandleAsync_MultipleSections_AddsToTheTargetedSectionOnly()
    {
        var angebot = _angebotRepository.Seed(Angebot.Create(1, null, "ANG-2026-00001", OwningInspectorId));
        var firstSection = angebot.AddSection("Pos. 1", 1);
        var secondSection = angebot.AddSection("Pos. 2", 2);
        AssignSectionId(firstSection, 1);
        AssignSectionId(secondSection, 2);
        var command = new AddAngebotItemCommand(
            angebot.Id, secondSection.Id, null, "Item for Pos. 2", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await _handler.HandleAsync(command, CancellationToken.None);

        Assert.Empty(firstSection.Items);
        Assert.Single(secondSection.Items);
    }

    // ---- Not found / ownership / state guard ------------------------------

    [Fact]
    public async Task HandleAsync_AngebotDoesNotExist_ThrowsNotFoundException()
    {
        var command = new AddAngebotItemCommand(999, 1, null, "Item", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_SectionDoesNotExist_ThrowsNotFoundException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, SectionId: 999, null, "Item", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_CatalogItemDoesNotExist_ThrowsNotFoundException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, CatalogItemId: 999, null, null, null, 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InspectorDoesNotOwnAngebot_ThrowsForbiddenException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "Item", null, "Stk", 1m, 10.00m, VatRate.Standard, InspectorId: 999);

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AngebotInReview_PropagatesDomainGuardFailure()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        angebot.AddItemToSection(section, "Seed item", 1m, ItemUnit.Piece(), Money.FromExact(1.00m), VatRate.Standard);
        angebot.SubmitForReview(); // now InReview — locked from editing
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "Too late", null, "Stk", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_CustomPathMissingDescriptionAndUnit_ThrowsValidationException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "", null, "", 1m, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Empty(section.Items);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_NonPositiveQuantity_ThrowsValidationException(decimal quantity)
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "Item", null, "Stk", quantity, 10.00m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NegativeUnitPrice_ThrowsValidationException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "Item", null, "Stk", 1m, -1m, VatRate.Standard, OwningInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InvalidVatRate_ThrowsValidationException()
    {
        var (angebot, section) = SeedDraftAngebotWithSection();
        AssignSectionId(section, 1);
        var command = new AddAngebotItemCommand(angebot.Id, section.Id, null, "Item", null, "Stk", 1m, 10.00m, (VatRate)999, OwningInspectorId);

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(command, CancellationToken.None));
    }
}
