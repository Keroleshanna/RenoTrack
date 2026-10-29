using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Angebote.Commands.UpdateAngebotItem;

/// <summary>
/// Corrects a line on an editable Angebot. The mirror of
/// <c>RemoveAngebotItemCommandHandler</c> in shape, including how it resolves the section.
/// </summary>
/// <remarks>
/// <para>
/// Returns the refreshed <c>AngebotSummaryDto</c> rather than the item, exactly as removal does:
/// changing a quantity or a price moves the document's totals and its VAT breakdown, and those are
/// what the screen must re-render. Returning only the line would invite the client to patch its
/// own copy of the money (D81).
/// </para>
/// <para>
/// Owning Inspector or any Admin (D115). Audited only when an Admin edits, matching every other
/// line-level edit: an Inspector correcting their own draft line is in-progress editing (CLAUDE.md
/// §10), while an Admin changing someone else's document is recorded as
/// <c>AngebotEditedByAdmin</c>.
/// </para>
/// </remarks>
public sealed class UpdateAngebotItemCommandHandler(
    IValidator<UpdateAngebotItemCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<UpdateAngebotItemCommand, AngebotSummaryDto>
{
    public async Task<AngebotSummaryDto> HandleAsync(
        UpdateAngebotItemCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        if (command.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        AngebotFreshness.EnsureExpectedVersion(angebot, command.ExpectedVersion);

        // Found by asking which section holds the item, rather than trusting a caller-supplied
        // section id that could name a different section of the same Angebot.
        var section = angebot.Sections.SingleOrDefault(s => s.Items.Any(i => i.Id == command.ItemId))
            ?? throw new NotFoundException(nameof(AngebotItem), command.ItemId);

        var item = section.Items.Single(i => i.Id == command.ItemId);

        // ItemUnit.FromCode rejects an unrecognised code with an ArgumentException, which the
        // middleware maps to 400 — the same path the add command takes.
        angebot.UpdateItem(
            section,
            item,
            command.Description,
            command.Quantity,
            ItemUnit.FromCode(command.UnitCode),
            Money.FromExact(command.UnitPrice),
            command.VatRate,
            command.Specification);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.RequestingInspectorId is null)
        {
            await auditService.LogAsync(
                entityType: nameof(Angebot),
                entityId: angebot.Id,
                action: AuditAction.AngebotEditedByAdmin,
                performedByUserId: command.PerformedByUserId,
                details: AngebotAdminEditAudit.Details(AngebotEditOperation.ItemUpdated, section.Id, item.Id),
                cancellationToken);
        }

        return angebot.ToSummaryDto();
    }
}
