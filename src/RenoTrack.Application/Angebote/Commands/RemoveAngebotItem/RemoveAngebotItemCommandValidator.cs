using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;

public sealed class RemoveAngebotItemCommandValidator : AbstractValidator<RemoveAngebotItemCommand>
{
    public RemoveAngebotItemCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ItemId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
    }
}
