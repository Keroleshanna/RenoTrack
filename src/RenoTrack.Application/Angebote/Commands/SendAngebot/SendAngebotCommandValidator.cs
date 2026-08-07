using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.SendAngebot;

/// <summary>
/// Shape only (CLAUDE.md §5): both ids must be plausible. Everything else this command depends on
/// — the Angebot being <c>ApprovedInternally</c>, the Lead being <c>AngebotInProgress</c> — needs
/// a loaded aggregate and therefore belongs to the Domain's own guards, not here.
/// </summary>
public sealed class SendAngebotCommandValidator : AbstractValidator<SendAngebotCommand>
{
    public SendAngebotCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.SentByAdminId).GreaterThan(0);
    }
}
