using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

public sealed class AddAngebotSectionCommandValidator : AbstractValidator<AddAngebotSectionCommand>
{
    public AddAngebotSectionCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.Title).NotEmpty();
        RuleFor(c => c.InspectorId).GreaterThan(0);
    }
}
