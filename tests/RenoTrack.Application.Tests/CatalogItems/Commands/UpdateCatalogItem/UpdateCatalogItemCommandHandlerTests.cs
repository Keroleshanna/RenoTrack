using FluentValidation;
using RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.CatalogItems.Commands.UpdateCatalogItem;

public class UpdateCatalogItemCommandHandlerTests
{
    private readonly FakeCatalogItemRepository _catalogItemRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly UpdateCatalogItemCommandHandler _handler;

    public UpdateCatalogItemCommandHandlerTests()
    {
        _handler = new UpdateCatalogItemCommandHandler(
            new UpdateCatalogItemCommandValidator(),
            _catalogItemRepository,
            _unitOfWork,
            _auditService);
    }

    private CatalogItem SeedExistingCatalogItem() =>
        _catalogItemRepository.Seed(CatalogItem.Create("Fliesen verlegen", ItemUnit.FromCode("m2"), Money.FromExact(45.50m), "Feinsteinzeug"));

    private static UpdateCatalogItemCommand ValidCommand(int id, int updatedByAdminUserId = 1) =>
        new(id, "Fliesen verlegen (Premium)", "Stk", 55.00m, "Feinsteinzeug, rutschfest", updatedByAdminUserId);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsCatalogItemDtoWithUpdatedValues()
    {
        var catalogItem = SeedExistingCatalogItem();

        var result = await _handler.HandleAsync(ValidCommand(catalogItem.Id), CancellationToken.None);

        Assert.Equal("Fliesen verlegen (Premium)", result.Title);
        Assert.Equal("Stk", result.DefaultUnit);
        Assert.Equal(55.00m, result.SuggestedUnitPrice);
        Assert.Equal("Feinsteinzeug, rutschfest", result.DefaultSpecification);
    }

    [Fact]
    public async Task HandleAsync_DoesNotChangeCreatedFromAngebotItemIdOrCreatedAtOrIsRetired()
    {
        var catalogItem = SeedExistingCatalogItem();
        var originalCreatedAt = catalogItem.CreatedAt;

        var result = await _handler.HandleAsync(ValidCommand(catalogItem.Id), CancellationToken.None);

        Assert.Null(result.CreatedFromAngebotItemId);
        Assert.Equal(originalCreatedAt, result.CreatedAt);
        Assert.False(result.IsRetired);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var catalogItem = SeedExistingCatalogItem();

        await _handler.HandleAsync(ValidCommand(catalogItem.Id), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsCatalogItemUpdatedAudit()
    {
        var catalogItem = SeedExistingCatalogItem();

        await _handler.HandleAsync(ValidCommand(catalogItem.Id, updatedByAdminUserId: 7), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("CatalogItem", call.EntityType);
        Assert.Equal(catalogItem.Id, call.EntityId);
        Assert.Equal(AuditAction.CatalogItemUpdated, call.Action);
        Assert.Equal(7, call.PerformedByUserId);
    }

    // ---- Existence ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_CatalogItemDoesNotExist_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(ValidCommand(id: 999), CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects()
    {
        var catalogItem = SeedExistingCatalogItem();
        var invalidCommand = ValidCommand(catalogItem.Id) with { Title = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(invalidCommand, CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Equal("Fliesen verlegen", catalogItem.Title); // unchanged
    }

    [Fact]
    public async Task HandleAsync_NegativeSuggestedUnitPrice_ThrowsValidationException()
    {
        var catalogItem = SeedExistingCatalogItem();
        var invalidCommand = ValidCommand(catalogItem.Id) with { SuggestedUnitPrice = -1m };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(invalidCommand, CancellationToken.None));
    }
}
