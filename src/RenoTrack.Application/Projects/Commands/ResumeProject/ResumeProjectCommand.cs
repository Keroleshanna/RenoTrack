using FluentValidation;

namespace RenoTrack.Application.Projects.Commands.ResumeProject;

/// <summary>
/// Resumes a paused Project (<c>PermissionMatrix.md</c> §5 "Put Project On Hold / Resume — Admin
/// F", StateMachine.md §4.3 <c>OnHold → Active</c>) — the mirror of
/// <c>PutProjectOnHoldCommand</c>.
/// </summary>
/// <param name="ResumedByAdminId">The acting Admin, from the JWT, for the audit entry (D61).</param>
public sealed record ResumeProjectCommand(int ProjectId, int ResumedByAdminId);

public sealed class ResumeProjectCommandValidator : AbstractValidator<ResumeProjectCommand>
{
    public ResumeProjectCommandValidator()
    {
        RuleFor(c => c.ProjectId).GreaterThan(0);
        RuleFor(c => c.ResumedByAdminId).GreaterThan(0);
    }
}
