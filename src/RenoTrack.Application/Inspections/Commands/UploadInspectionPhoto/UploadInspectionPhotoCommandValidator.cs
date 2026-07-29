using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;

public sealed class UploadInspectionPhotoCommandValidator : AbstractValidator<UploadInspectionPhotoCommand>
{
    public UploadInspectionPhotoCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.UploadedByInspectorId).GreaterThan(0);
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.FileContent).NotNull();
    }
}
