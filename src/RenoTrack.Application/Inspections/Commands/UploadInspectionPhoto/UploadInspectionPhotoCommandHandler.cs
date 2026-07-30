using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;

/// <summary>
/// Sequence Diagram §3 Step B. No AuditLog entry — operational activity, not a business
/// workflow transition (the Sequence Diagram itself omits an audit step here).
///
/// The FileUrl is computed here, before calling Inspection.AddPhoto, specifically so BR-10's
/// guard runs (and can reject) before any actual file I/O happens — uploading first and
/// discovering the rejection afterward would waste an irreversible external side effect and
/// leave an orphaned file in storage every time someone tries to upload to an already-completed
/// Inspection. See IFileStorage's remarks for the same reasoning from the storage side.
/// </summary>
public sealed class UploadInspectionPhotoCommandHandler(
    IValidator<UploadInspectionPhotoCommand> validator,
    IInspectionRepository inspectionRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    IOwnershipValidator ownershipValidator) : ICommandHandler<UploadInspectionPhotoCommand, PhotoDto>
{
    public async Task<PhotoDto> HandleAsync(UploadInspectionPhotoCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        ownershipValidator.EnsureInspectionOwnership(inspection, command.UploadedByInspectorId);

        var fileUrl = $"inspections/{inspection.Id}/{Guid.NewGuid()}{Path.GetExtension(command.FileName)}";

        var photo = inspection.AddPhoto(fileUrl, command.Caption); // BR-10 guard fires here — before any I/O

        await fileStorage.SaveAsync(command.FileContent, fileUrl, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return photo.ToDto();
    }
}
