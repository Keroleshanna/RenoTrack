using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.ReopenInspection;

/// <summary>
/// Reopens a completed Inspection so its record can be corrected (BR-10's own named remedy).
/// </summary>
/// <param name="ReopenedByInspectorId">
/// The assigned Inspector, from the JWT. Scoped ("S"), like every other edit on an Inspection.
/// </param>
public sealed record ReopenInspectionCommand(int InspectionId, int ReopenedByInspectorId);

public sealed class ReopenInspectionCommandValidator : AbstractValidator<ReopenInspectionCommand>
{
    public ReopenInspectionCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.ReopenedByInspectorId).GreaterThan(0);
    }
}
