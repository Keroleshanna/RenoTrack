using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;

/// <summary>No rule on Notes itself — optional free text, no format to enforce; Inspection.UpdateNotes already allows clearing it to null.</summary>
public sealed class UpdateInspectionNotesCommandValidator : AbstractValidator<UpdateInspectionNotesCommand>
{
    public UpdateInspectionNotesCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.UpdatedByInspectorId).GreaterThan(0);
    }
}
