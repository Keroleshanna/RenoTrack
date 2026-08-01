using FluentValidation;
using Microsoft.Extensions.Logging;
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
    IOwnershipValidator ownershipValidator,
    ILogger<UploadInspectionPhotoCommandHandler> logger) : ICommandHandler<UploadInspectionPhotoCommand, PhotoDto>
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

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The file is already on disk and the row will never exist, so remove it. Note the
            // ordering above is still the right way round: committing first would trade this
            // orphaned file for a database row pointing at a file that was never written — an
            // invisible, inert leak versus visible breakage every time the dashboard renders it.
            await TryCompensateAsync(fileUrl, cancellationToken);
            throw;
        }

        return photo.ToDto();
    }

    /// <summary>
    /// Best-effort removal of a file whose database row failed to commit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is compensation, not atomicity, and must not be described as guaranteeing
    /// filesystem/database consistency.</b> No transaction can span SQL Server and a filesystem. If
    /// the process dies between the write and this call, the orphan survives; if the delete itself
    /// fails, the orphan survives. What this buys is that the ordinary, recoverable case — a commit
    /// that fails while the process is healthy — does not leak a file.
    /// </para>
    /// <para>
    /// Failures here are logged and swallowed so the original commit exception stays the caller's
    /// answer. Letting a cleanup failure surface instead would replace an accurate report of what
    /// went wrong with a misleading one — the same reasoning behind the Best-Effort Audit strategy
    /// (D50).
    /// </para>
    /// </remarks>
    private async Task TryCompensateAsync(string fileUrl, CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteAsync(fileUrl, cancellationToken);
        }
        catch (Exception compensationFailure)
        {
            logger.LogError(
                compensationFailure,
                "Failed to remove {FileUrl} after its database commit failed; it is now an orphaned file.",
                fileUrl);
        }
    }
}
