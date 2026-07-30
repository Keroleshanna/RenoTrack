using FluentValidation;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.CatalogItems.Commands.UpdateCatalogItem;

/// <summary>
/// PermissionMatrix.md §6 marks Catalog editing Admin-"F" — no IOwnershipValidator call, same
/// reasoning as CreateCatalogItemCommandHandler. No notification: SRS FR-9.2 names no
/// Catalog-related trigger.
/// </summary>
public sealed class UpdateCatalogItemCommandHandler(
    IValidator<UpdateCatalogItemCommand> validator,
    ICatalogItemRepository catalogItemRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<UpdateCatalogItemCommand, CatalogItemDto>
{
    public async Task<CatalogItemDto> HandleAsync(UpdateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var catalogItem = await catalogItemRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CatalogItem), command.Id);

        catalogItem.Update(
            command.Title,
            ItemUnit.FromCode(command.DefaultUnitCode),
            Money.FromExact(command.SuggestedUnitPrice),
            command.DefaultSpecification);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(CatalogItem),
            entityId: catalogItem.Id,
            action: AuditAction.CatalogItemUpdated,
            performedByUserId: command.UpdatedByAdminUserId,
            details: null,
            cancellationToken);

        return catalogItem.ToDto();
    }
}
