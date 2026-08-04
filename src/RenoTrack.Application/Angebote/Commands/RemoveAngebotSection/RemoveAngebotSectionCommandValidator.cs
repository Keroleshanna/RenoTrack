using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;

public sealed class RemoveAngebotSectionCommandValidator : AbstractValidator<RemoveAngebotSectionCommand>
{
    public RemoveAngebotSectionCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.SectionId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
    }
}
