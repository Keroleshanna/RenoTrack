using FluentValidation;

namespace RenoTrack.Application.Leads.Commands.AssignLeadInspector;

/// <summary>
/// Shape only (CLAUDE.md §5): three ids that must be plausible. Whether
/// <c>InspectorId</c> names a real, active Inspector is a business question requiring a lookup, so
/// it belongs to the handler via <c>IUserQueries</c> and never to a validator.
/// </summary>
public sealed class AssignLeadInspectorCommandValidator : AbstractValidator<AssignLeadInspectorCommand>
{
    public AssignLeadInspectorCommandValidator()
    {
        RuleFor(c => c.LeadId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
        RuleFor(c => c.AssignedByAdminId).GreaterThan(0);
    }
}
