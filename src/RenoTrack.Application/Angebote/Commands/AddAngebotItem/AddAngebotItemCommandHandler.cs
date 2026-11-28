using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotItem;

/// <summary>
/// Sequence Diagram §4 "Add a Line Item" (both the custom and Catalog-sourced paths). Owning
/// Inspector or any Admin (D115) — the Admin uses the Catalog picker while editing
/// (<c>PermissionMatrix.md</c> §3). Audited only when an Admin edits, exactly as
/// AddAngebotSectionCommandHandler; an Inspector's own line is in-progress work. No notification:
/// SRS FR-9.2 names no trigger for this. BR-14: a retired CatalogItem is still a valid
/// CatalogItemId target — GetByIdAsync is deliberately not filtered by IsRetired here, unlike
/// ICatalogItemQueries.SearchAsync.
/// </summary>
public sealed class AddAngebotItemCommandHandler(
    IValidator<AddAngebotItemCommand> validator,
    IAngebotRepository angebotRepository,
    ICatalogItemRepository catalogItemRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<AddAngebotItemCommand, AddAngebotItemResult>
{
    public async Task<AddAngebotItemResult> HandleAsync(AddAngebotItemCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        if (command.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        AngebotFreshness.EnsureExpectedVersion(angebot, command.ExpectedVersion);

        // Pre-EF (Phase 3), freshly-created sections all share Id == 0 — see CLAUDE.md §2. This
        // resolution becomes unambiguous once real ids exist; test fixtures assign distinguishing
        // ids via the same sanctioned test-only reflection pattern used for aggregate roots.
        var section = angebot.Sections.FirstOrDefault(s => s.Id == command.SectionId)
            ?? throw new NotFoundException(nameof(AngebotSection), command.SectionId);

        string description;
        string? specification;
        ItemUnit unit;

        if (command.CatalogItemId is int catalogItemId)
        {
            var catalogItem = await catalogItemRepository.GetByIdAsync(catalogItemId, cancellationToken)
                ?? throw new NotFoundException(nameof(CatalogItem), catalogItemId);

            description = catalogItem.Title;
            specification = catalogItem.DefaultSpecification;
            unit = catalogItem.DefaultUnit;
        }
        else
        {
            description = command.Description!;
            specification = command.Specification;
            unit = ItemUnit.FromCode(command.UnitCode!);
        }

        var item = angebot.AddItemToSection(
            section,
            description,
            command.Quantity,
            unit,
            Money.FromExact(command.UnitPrice),
            command.VatRate,
            specification,
            command.CatalogItemId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.RequestingInspectorId is null)
        {
            await auditService.LogAsync(
                entityType: nameof(Angebot),
                entityId: angebot.Id,
                action: AuditAction.AngebotEditedByAdmin,
                performedByUserId: command.PerformedByUserId,
                details: AngebotAdminEditAudit.Details(AngebotEditOperation.ItemAdded, section.Id, item.Id),
                cancellationToken);
        }

        return new AddAngebotItemResult(item.ToDto(), angebot.ToSummaryDto());
    }
}
