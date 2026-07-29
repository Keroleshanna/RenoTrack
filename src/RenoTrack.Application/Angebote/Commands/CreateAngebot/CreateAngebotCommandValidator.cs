using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.CreateAngebot;

public sealed class CreateAngebotCommandValidator : AbstractValidator<CreateAngebotCommand>
{
    public CreateAngebotCommandValidator()
    {
        RuleFor(c => c.LeadId).GreaterThan(0);
        RuleFor(c => c.CreatedByInspectorId).GreaterThan(0);
        When(c => c.InspectionId.HasValue, () =>
        {
            RuleFor(c => c.InspectionId!.Value).GreaterThan(0);
        });
    }
}
