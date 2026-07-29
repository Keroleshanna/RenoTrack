using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;

/// <summary>
/// Sequence Diagram §3 Step B. The only Inspection command that never touches Lead at all — no
/// status transition results from updating notes. No AuditLog entry, same reasoning as
/// UploadInspectionPhotoCommand (operational activity, not a workflow transition).
/// </summary>
public sealed class UpdateInspectionNotesCommandHandler(
    IValidator<UpdateInspectionNotesCommand> validator,
    IInspectionRepository inspectionRepository,
    IUnitOfWork unitOfWork,
    IOwnershipValidator ownershipValidator) : ICommandHandler<UpdateInspectionNotesCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(UpdateInspectionNotesCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        ownershipValidator.EnsureInspectionOwnership(inspection, command.UpdatedByInspectorId);

        inspection.UpdateNotes(command.Notes); // BR-10 self-guard

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return inspection.ToDto();
    }
}
