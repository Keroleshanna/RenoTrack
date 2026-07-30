using FluentValidation;
using RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.CatalogItems.Commands.RetireCatalogItem;

public class RetireCatalogItemCommandHandlerTests
{
    private readonly FakeCatalogItemRepository _catalogItemRepository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly RetireCatalogItemCommandHandler _handler;

    public RetireCatalogItemCommandHandlerTests()
    {
        _handler = new RetireCatalogItemCommandHandler(
            new RetireCatalogItemCommandValidator(),
            _catalogItemRepository,
            _unitOfWork,
            _auditService);
    }

    private CatalogItem SeedExistingCatalogItem() =>
        _catalogItemRepository.Seed(CatalogItem.Create("Fliesen verlegen", ItemUnit.FromCode("m2"), Money.FromExact(45.50m)));

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ReturnsCatalogItemDtoWithIsRetiredTrue()
    {
        var catalogItem = SeedExistingCatalogItem();

        var result = await _handler.HandleAsync(new RetireCatalogItemCommand(catalogItem.Id, RetiredByAdminUserId: 1), CancellationToken.None);

        Assert.True(result.IsRetired);
    }

    [Fact]
    public async Task HandleAsync_SavesChangesExactlyOnce()
    {
        var catalogItem = SeedExistingCatalogItem();

        await _handler.HandleAsync(new RetireCatalogItemCommand(catalogItem.Id, 1), CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_LogsCatalogItemRetiredAudit()
    {
        var catalogItem = SeedExistingCatalogItem();

        await _handler.HandleAsync(new RetireCatalogItemCommand(catalogItem.Id, RetiredByAdminUserId: 7), CancellationToken.None);

        var call = Assert.Single(_auditService.Calls);
        Assert.Equal("CatalogItem", call.EntityType);
        Assert.Equal(catalogItem.Id, call.EntityId);
        Assert.Equal(AuditAction.CatalogItemRetired, call.Action);
        Assert.Equal(7, call.PerformedByUserId);
    }

    // ---- Idempotency (BR-12) ----------------------------------------------

    [Fact]
    public async Task HandleAsync_RetiringAnAlreadyRetiredCatalogItem_SucceedsWithoutError()
    {
        var catalogItem = SeedExistingCatalogItem();
        catalogItem.Retire();

        var result = await _handler.HandleAsync(new RetireCatalogItemCommand(catalogItem.Id, 1), CancellationToken.None);

        Assert.True(result.IsRetired);
    }

    // ---- Existence ----------------------------------------------------

    [Fact]
    public async Task HandleAsync_CatalogItemDoesNotExist_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.HandleAsync(new RetireCatalogItemCommand(999, 1), CancellationToken.None));
    }

    // ---- Validation ----------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task HandleAsync_InvalidCommand_ThrowsAndPerformsNoSideEffects(int id, int retiredByAdminUserId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(new RetireCatalogItemCommand(id, retiredByAdminUserId), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
    }
}
