using FluentValidation;

namespace RenoTrack.Application.Inspections.Commands.ScheduleInspection;

/// <summary>Shape validation only — existence of the Lead/Inspector and the Domain's own Status guard are handled in the handler/Domain, not here.</summary>
public sealed class ScheduleInspectionCommandValidator : AbstractValidator<ScheduleInspectionCommand>
{
    public ScheduleInspectionCommandValidator()
    {
        RuleFor(c => c.LeadId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
        RuleFor(c => c.ScheduledByAdminId).GreaterThan(0);
        RuleFor(c => c.ScheduledAt).NotEqual(default(DateTime));
    }
}
