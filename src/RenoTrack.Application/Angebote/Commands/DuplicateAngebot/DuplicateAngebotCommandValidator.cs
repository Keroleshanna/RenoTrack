using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.DuplicateAngebot;

public sealed class DuplicateAngebotCommandValidator : AbstractValidator<DuplicateAngebotCommand>
{
    public DuplicateAngebotCommandValidator()
    {
        RuleFor(c => c.SourceAngebotId).GreaterThan(0);
        RuleFor(c => c.TargetLeadId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
    }
}
