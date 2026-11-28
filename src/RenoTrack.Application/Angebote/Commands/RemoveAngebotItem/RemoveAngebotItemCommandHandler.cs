using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;

/// <summary>
/// Removes one line from an editable Angebot. Owning Inspector or any Admin (D115); audited only
/// when an Admin edits, like every builder mutation.
/// </summary>
public sealed class RemoveAngebotItemCommandHandler(
    IValidator<RemoveAngebotItemCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<RemoveAngebotItemCommand, AngebotSummaryDto>
{
    public async Task<AngebotSummaryDto> HandleAsync(
        RemoveAngebotItemCommand command,
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

        // The section is found by asking which one holds the item, rather than trusting a
        // caller-supplied section id that could name a different section of the same Angebot.
        var section = angebot.Sections.SingleOrDefault(s => s.Items.Any(i => i.Id == command.ItemId))
            ?? throw new NotFoundException(nameof(AngebotItem), command.ItemId);

        var item = section.Items.Single(i => i.Id == command.ItemId);

        angebot.RemoveItem(section, item);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.RequestingInspectorId is null)
        {
            await auditService.LogAsync(
                entityType: nameof(Angebot),
                entityId: angebot.Id,
                action: AuditAction.AngebotEditedByAdmin,
                performedByUserId: command.PerformedByUserId,
                details: AngebotAdminEditAudit.Details(AngebotEditOperation.ItemRemoved, section.Id, command.ItemId),
                cancellationToken);
        }

        return angebot.ToSummaryDto();
    }
}
