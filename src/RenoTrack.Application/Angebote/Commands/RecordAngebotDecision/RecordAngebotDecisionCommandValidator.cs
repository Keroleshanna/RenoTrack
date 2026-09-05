using FluentValidation;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;

/// <summary>
/// Shape only (CLAUDE.md §5): a token is present and the decision is one of the two defined
/// values. Token *format* is deliberately unvalidated, for the same reason as the read query — a
/// length or character-class rule here would be a second, quieter definition of what a token looks
/// like, competing with the generator's.
///
/// <c>IsInEnum</c> matters more than usual here: without it, an unmapped integer would sail through
/// as a valid <see cref="CustomerDecision"/> and reach the handler's switch, which must then either
/// guess or throw something unmapped.
///
/// <para>
/// <b>Both reason rules are shape, not business logic</b> (D98). Whether a string is too long, and
/// whether a field is meaningful for the chosen action, are answerable from the request alone — no
/// aggregate is loaded and no repository is touched, which is exactly the line §5 draws. Contrast
/// <c>CompleteProjectCommandHandler</c>, whose sibling rule needs the Project's invoices before it
/// can tell whether there is anything to override, and therefore lives in the handler.
/// </para>
/// <para>
/// <b>The length rule here is a friendly front door, not the protection.</b>
/// <c>Angebot.RecordCustomerRejection</c> enforces the same limit itself and is the backstop (§5).
/// The number is read from the aggregate rather than repeated, so the two cannot drift.
/// </para>
/// </summary>
public sealed class RecordAngebotDecisionCommandValidator : AbstractValidator<RecordAngebotDecisionCommand>
{
    public RecordAngebotDecisionCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.Decision).IsInEnum();

        RuleFor(c => c.Reason)
            .MaximumLength(Angebot.MaxDecisionReasonLength);

        // An approval has nothing to justify, so a reason sent with one is refused rather than
        // dropped — accepting a value and discarding it is the pattern this codebase has now
        // refused three times (Phase 6's own gap, K-4/D67, and D98).
        //
        // IsNullOrWhiteSpace rather than FluentValidation's Empty(): "   " carries no content, so
        // nothing is discarded by ignoring it, and the aggregate already normalises blank to null.
        // Refusing whitespace here would make the API stricter than the rule it is enforcing.
        RuleFor(c => c.Reason)
            .Must(string.IsNullOrWhiteSpace)
            .When(c => c.Decision == CustomerDecision.Approve)
            .WithMessage("A reason may only be given when rejecting an Angebot.");
    }
}
