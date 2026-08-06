using FluentValidation;

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
/// </summary>
public sealed class RecordAngebotDecisionCommandValidator : AbstractValidator<RecordAngebotDecisionCommand>
{
    public RecordAngebotDecisionCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.Decision).IsInEnum();
    }
}
