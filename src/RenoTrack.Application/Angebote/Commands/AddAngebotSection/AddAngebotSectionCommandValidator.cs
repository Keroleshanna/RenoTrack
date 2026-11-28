using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

public sealed class AddAngebotSectionCommandValidator : AbstractValidator<AddAngebotSectionCommand>
{
    public AddAngebotSectionCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.Title).NotEmpty();
        RuleFor(c => c.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(c => c.RequestingInspectorId).GreaterThan(0).When(c => c.RequestingInspectorId is not null);
        RuleFor(c => c.PerformedByUserId).GreaterThan(0);
    }
}
