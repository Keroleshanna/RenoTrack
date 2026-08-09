using FluentValidation;

namespace RenoTrack.Application.Projects.Commands.CompleteProject;

/// <summary>
/// Marks a Project <c>Completed</c> (SRS FR-7.3, StateMachine.md §4.3, Sequence Diagram §10), with
/// FR-8.6's explicit override path for a Project whose Invoices would otherwise block it.
///
/// <para>
/// <c>ForceOverride</c> and <c>Reason</c> are the two fields Sequence Diagram §10 sends
/// (<c>{ forceOverride: true, reason }</c>). <c>ProjectId</c> comes from the route and
/// <c>CompletedByAdminId</c> from the authenticated principal, never the body (D61).
/// </para>
/// </summary>
public sealed record CompleteProjectCommand(
    int ProjectId,
    bool ForceOverride,
    string? Reason,
    int CompletedByAdminId);

/// <summary>
/// Shape only, never business state (CLAUDE.md §5) — every rule here is decidable from the request
/// alone, with no aggregate loaded.
///
/// <para>
/// <b>The two cross-field rules are symmetric on purpose.</b> FR-8.6 makes a reason mandatory for
/// an override, so <c>ForceOverride</c> without one is a field-level 400. The mirror — a reason
/// supplied while <c>ForceOverride</c> is false — is <b>also</b> rejected rather than ignored
/// (Phase 8 Slice 6, decision K-4): silently discarding it would break the same expectation that
/// made accepting-and-discarding the FR-6.3 rejection reason unacceptable in Phase 6, and it would
/// let a caller believe they had recorded a justification that was never stored anywhere.
/// </para>
/// <para>
/// <b>Whitespace is not a reason.</b> <c>NotEmpty()</c> alone accepts <c>" "</c>, so the rule
/// trims first — a blank override justification satisfies FR-8.6 in form and not at all in
/// substance.
/// </para>
/// <para>
/// <b>"Overriding when nothing is blocked" is deliberately not here.</b> That needs the Project's
/// Invoices, so it belongs in the handler, not in an <c>AbstractValidator</c> (CLAUDE.md §5).
/// </para>
/// </summary>
public sealed class CompleteProjectCommandValidator : AbstractValidator<CompleteProjectCommand>
{
    public CompleteProjectCommandValidator()
    {
        RuleFor(c => c.ProjectId).GreaterThan(0);
        RuleFor(c => c.CompletedByAdminId).GreaterThan(0);

        RuleFor(c => c.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .When(c => c.ForceOverride)
            .WithMessage("An override requires a reason (FR-8.6).");

        RuleFor(c => c.Reason)
            .Must(string.IsNullOrEmpty)
            .When(c => !c.ForceOverride)
            .WithMessage("A reason is only accepted together with 'forceOverride'.");
    }
}
