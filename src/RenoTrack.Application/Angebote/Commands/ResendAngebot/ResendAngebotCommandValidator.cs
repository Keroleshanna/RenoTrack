using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.ResendAngebot;

/// <summary>
/// Shape only (CLAUDE.md §5): two positive ids. Whether the Angebot is actually <c>Sent</c>, and
/// whether its link is still supersedable, are questions about loaded state and belong to the
/// handler and the aggregate respectively — never here, because a validator must not query.
/// </summary>
public sealed class ResendAngebotCommandValidator : AbstractValidator<ResendAngebotCommand>
{
    public ResendAngebotCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ResentByAdminId).GreaterThan(0);
    }
}
