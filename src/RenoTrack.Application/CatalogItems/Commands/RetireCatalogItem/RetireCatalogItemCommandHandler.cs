using FluentValidation;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;

/// <summary>
/// PermissionMatrix.md §6 marks Catalog retirement Admin-"F" — no IOwnershipValidator call,
/// same reasoning as CreateCatalogItemCommandHandler/UpdateCatalogItemCommandHandler. No
/// notification: SRS FR-9.2 names no Catalog-related trigger. CatalogItem.Retire() is
/// idempotent, so there is no Domain guard failure to propagate here.
/// </summary>
public sealed class RetireCatalogItemCommandHandler(
    IValidator<RetireCatalogItemCommand> validator,
    ICatalogItemRepository catalogItemRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<RetireCatalogItemCommand, CatalogItemDto>
{
    public async Task<CatalogItemDto> HandleAsync(RetireCatalogItemCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var catalogItem = await catalogItemRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(CatalogItem), command.Id);

        catalogItem.Retire();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(CatalogItem),
            entityId: catalogItem.Id,
            action: AuditAction.CatalogItemRetired,
            performedByUserId: command.RetiredByAdminUserId,
            details: null,
            cancellationToken);

        return catalogItem.ToDto();
    }
}
