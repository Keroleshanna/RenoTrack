using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;

public sealed class RemoveAngebotItemCommandHandler(
    IValidator<RemoveAngebotItemCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveAngebotItemCommand, AngebotSummaryDto>
{
    public async Task<AngebotSummaryDto> HandleAsync(
        RemoveAngebotItemCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        ownershipValidator.EnsureAngebotOwnership(angebot, command.InspectorId);

        // The section is found by asking which one holds the item, rather than trusting a
        // caller-supplied section id that could name a different section of the same Angebot.
        var section = angebot.Sections.SingleOrDefault(s => s.Items.Any(i => i.Id == command.ItemId))
            ?? throw new NotFoundException(nameof(AngebotItem), command.ItemId);

        var item = section.Items.Single(i => i.Id == command.ItemId);

        angebot.RemoveItem(section, item);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return angebot.ToSummaryDto();
    }
}
