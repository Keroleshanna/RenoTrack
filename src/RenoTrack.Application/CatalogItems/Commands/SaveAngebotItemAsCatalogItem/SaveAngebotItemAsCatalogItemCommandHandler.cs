using FluentValidation;
using RenoTrack.Application.Angebote;
using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;

/// <summary>
/// Copies the item's current values into a new Catalog entry and records where it came from.
/// </summary>
/// <remarks>
/// <para>
/// <b>No ownership check, deliberately.</b> PermissionMatrix.md §3 marks "Save item as new Catalog
/// entry" as Inspector "F" — any Inspector may contribute, because the Catalog is shared
/// company-wide rather than scoped to a Lead. Calling <c>IOwnershipValidator</c> here would be a
/// semantic error (CLAUDE.md §16), not merely redundant.
/// </para>
/// <para>
/// The copy is a snapshot, which is BR-8 working in the other direction: an AngebotItem never
/// follows its Catalog entry, and a Catalog entry never follows the item it was born from. Quantity
/// and VAT rate are deliberately not copied — they are facts about one job, not properties of a
/// reusable template (ERD.md gives CatalogItem no column for either).
/// </para>
/// </remarks>
public sealed class SaveAngebotItemAsCatalogItemCommandHandler(
    IValidator<SaveAngebotItemAsCatalogItemCommand> validator,
    IAngebotQueries angebotQueries,
    ICatalogItemRepository catalogItemRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<SaveAngebotItemAsCatalogItemCommand, CatalogItemDto>
{
    public async Task<CatalogItemDto> HandleAsync(
        SaveAngebotItemAsCatalogItemCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var item = await angebotQueries.GetItemAsync(command.AngebotItemId, cancellationToken)
            ?? throw new NotFoundException(nameof(AngebotItem), command.AngebotItemId);

        // Idempotent, not a conflict. FR-4.10 is a one-click action on a line, and a double-click,
        // a retried request or a screen that has not refreshed must not put two entries into a
        // shared company library. Returning the existing entry makes a repeat harmless and is
        // precise, because the match is on this exact line's id rather than on a title anyone else
        // might legitimately reuse.
        //
        // A 409 was considered and rejected: the caller asked for "this line is in the Catalog",
        // and after a repeat that is exactly the state — refusing would report a failure for an
        // outcome that already holds.
        var existing = await catalogItemRepository.GetByCreatedFromAngebotItemIdAsync(
            command.AngebotItemId, cancellationToken);

        if (existing is not null)
        {
            return existing.ToDto();
        }

        var catalogItem = CatalogItem.Create(
            item.Description,
            ItemUnit.FromCode(item.Unit),
            Money.FromExact(item.UnitPrice),
            item.Specification,
            createdFromAngebotItemId: command.AngebotItemId);

        await catalogItemRepository.AddAsync(catalogItem, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The same milestone as any other Catalog entry appearing — what differs is who performed it
        // and the provenance link, both already captured. No new AuditAction value is warranted.
        await auditService.LogAsync(
            entityType: nameof(CatalogItem),
            entityId: catalogItem.Id,
            action: AuditAction.CatalogItemCreated,
            performedByUserId: command.SavedByInspectorId,
            details: null,
            cancellationToken);

        return catalogItem.ToDto();
    }
}
