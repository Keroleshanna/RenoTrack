using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.CompleteInspection;

public sealed class CompleteInspectionCommandValidator : AbstractValidator<CompleteInspectionCommand>
{
    public CompleteInspectionCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.CompletedByInspectorId).GreaterThan(0);
    }
}
