using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.ReassignInspection;

/// <summary>
/// Shape only (CLAUDE.md §5). Whether the target is a real, active Inspector needs a lookup and so
/// belongs to the handler; whether the Inspection is still open is a Domain invariant (BR-10) and
/// belongs to the aggregate.
/// </summary>
public sealed class ReassignInspectionCommandValidator : AbstractValidator<ReassignInspectionCommand>
{
    public ReassignInspectionCommandValidator()
    {
        RuleFor(c => c.InspectionId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
        RuleFor(c => c.ReassignedByAdminId).GreaterThan(0);
    }
}
