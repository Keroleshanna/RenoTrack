using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

/// <summary>
/// Sequence Diagram §4 "Add a Section". Editability (Draft/ChangesRequested only), the
/// implicit ChangesRequested→Draft transition, and totals recalculation all live entirely
/// inside Angebot.AddSection — not duplicated here. No AuditLog entry: adding a section is
/// operational construction activity, not a business milestone, even on the (rare) occasion
/// it triggers the internal ChangesRequested→Draft adjustment.
/// </summary>
public sealed class AddAngebotSectionCommandHandler(
    IValidator<AddAngebotSectionCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork) : ICommandHandler<AddAngebotSectionCommand, SectionDto>
{
    public async Task<SectionDto> HandleAsync(AddAngebotSectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        ownershipValidator.EnsureAngebotOwnership(angebot, command.InspectorId);

        var section = angebot.AddSection(command.Title, command.SortOrder);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return section.ToDto();
    }
}
