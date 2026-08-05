using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;

/// <summary>
/// Returns the Angebot summary rather than nothing, because removing a section changes NetTotal and
/// GrossTotal — the caller would otherwise have to re-read the Angebot to render the new totals.
/// This mirrors <c>AddAngebotItemResult</c>'s existing shape.
/// </summary>
public sealed class RemoveAngebotSectionCommandHandler(
    IValidator<RemoveAngebotSectionCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveAngebotSectionCommand, AngebotSummaryDto>
{
    public async Task<AngebotSummaryDto> HandleAsync(
        RemoveAngebotSectionCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        ownershipValidator.EnsureAngebotOwnership(angebot, command.InspectorId);

        // Resolved from the already-loaded aggregate, then passed by reference — the Domain method
        // takes the instance, not an id (Architecture.md §6, and the aggregate re-verifies it).
        var section = angebot.Sections.SingleOrDefault(s => s.Id == command.SectionId)
            ?? throw new NotFoundException(nameof(AngebotSection), command.SectionId);

        angebot.RemoveSection(section);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return angebot.ToSummaryDto();
    }
}
