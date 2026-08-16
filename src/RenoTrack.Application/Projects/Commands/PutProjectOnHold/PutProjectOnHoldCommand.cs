using FluentValidation;

namespace RenoTrack.Application.Projects.Commands.PutProjectOnHold;

/// <summary>
/// Pauses an active Project (<c>PermissionMatrix.md</c> §5 "Put Project On Hold / Resume — Admin
/// F", StateMachine.md §4.3 <c>Active → OnHold</c>).
/// </summary>
/// <param name="PutOnHoldByAdminId">The acting Admin, from the JWT, for the audit entry (D61).</param>
/// <remarks>
/// <b>No reason field, deliberately.</b> StateMachine.md §4.3's row notes "Reason optional/free
/// text", but <c>ERD.md</c>'s <c>PROJECT</c> defines no column to store one, and
/// <c>Project.PutOnHold()</c> accordingly accepts none. Taking a reason here would either discard
/// it silently or force it into the AuditLog's <c>details</c> as its only home — and D50 makes
/// audit writes best-effort and swallowed, so that is not storage a caller can rely on. The
/// override reason on <c>CompleteProjectCommand</c> lives there only because FR-8.6 makes it
/// mandatory; nothing makes this one mandatory, so no storage is invented for it. Recorded as a
/// known gap rather than quietly closed.
/// </remarks>
public sealed record PutProjectOnHoldCommand(int ProjectId, int PutOnHoldByAdminId);

public sealed class PutProjectOnHoldCommandValidator : AbstractValidator<PutProjectOnHoldCommand>
{
    public PutProjectOnHoldCommandValidator()
    {
        RuleFor(c => c.ProjectId).GreaterThan(0);
        RuleFor(c => c.PutOnHoldByAdminId).GreaterThan(0);
    }
}
