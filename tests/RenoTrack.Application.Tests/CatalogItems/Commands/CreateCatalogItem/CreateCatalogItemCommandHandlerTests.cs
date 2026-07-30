using FluentValidation;
using RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Tests.Fakes;

namespace RenoTrack.Application.Tests.CatalogItems.Commands.CreateCatalogItem;

public class CreateCatalogItemCommandHandlerTests
{
    private readonly FakeCatalogItemRepository _catalogItemRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly CreateCatalogItemCommandHandler _handler;

    public CreateCatalogItemCommandHandlerTests()
    {
        _handler = new CreateCatalogItemCommandHandler(
            new CreateCatalogItemCommandValidator(),
            _catalogItemRepository,
            _unitOfWork,
            _auditService);
    }

    private static CreateCatalogItemCommand ValidCommand(int createdByAdminUserId = 1) =>
        new("Fliesen verlegen", "m2", 45.50m, "Feinsteinzeug, rutschfest", createdByAdminUserId);

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsCatalogItemDtoWithSubmittedValues()
    {
        var result = await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal("Fliesen verlegen", result.Title);
        Assert.Equal("Feinsteinzeug, rutschfest", result.DefaultSpecification);
        Assert.Equal("m2", result.DefaultUnit);
        Assert.Equal(45.50m, result.SuggestedUnitPrice);
        Assert.Null(result.CreatedFromAngebotItemId);
        Assert.False(result.IsRetired);
    }

    [Fact]
    public async Task HandleAsync_AddsCatalogItemToRepository()
    {
        await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Single(_catalogItemRepository.AddedCatalogItems);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        await _handler.HandleAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsCatalogItemCreatedAudit()
    {
        var result = await _handler.HandleAsync(ValidCommand(createdByAdminUserId: 7), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("CatalogItem", call.EntityType);
        Assert.Equal(result.Id, call.EntityId);
        Assert.Equal(AuditAction.CatalogItemCreated, call.Action);
        Assert.Equal(7, call.PerformedByUserId);
    }

    // ---- Validation ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects()
    {
        var invalidCommand = ValidCommand() with { Title = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(invalidCommand, CancellationToken.None));

        Assert.Empty(_catalogItemRepository.AddedCatalogItems);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }

    [Fact]
    public async Task HandleAsync_NegativeSuggestedUnitPrice_ThrowsValidationException()
    {
        var invalidCommand = ValidCommand() with { SuggestedUnitPrice = -1m };

        await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(invalidCommand, CancellationToken.None));
    }
}
