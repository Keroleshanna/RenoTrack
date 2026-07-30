using FluentValidation;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.CatalogItems.Commands.CreateCatalogItem;

/// <summary>
/// PermissionMatrix.md §6 marks Catalog curation Admin-"F" (full access), not "S" — no
/// IOwnershipValidator call, same reasoning as ApproveAngebotCommandHandler. No notification:
/// SRS FR-9.2 names no Catalog-related trigger.
/// </summary>
public sealed class CreateCatalogItemCommandHandler(
    IValidator<CreateCatalogItemCommand> validator,
    ICatalogItemRepository catalogItemRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<CreateCatalogItemCommand, CatalogItemDto>
{
    public async Task<CatalogItemDto> HandleAsync(CreateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var catalogItem = CatalogItem.Create(
            command.Title,
            ItemUnit.FromCode(command.DefaultUnitCode),
            Money.FromExact(command.SuggestedUnitPrice),
            command.DefaultSpecification);

        await catalogItemRepository.AddAsync(catalogItem, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(CatalogItem),
            entityId: catalogItem.Id,
            action: AuditAction.CatalogItemCreated,
            performedByUserId: command.CreatedByAdminUserId,
            details: null,
            cancellationToken);

        return catalogItem.ToDto();
    }
}
