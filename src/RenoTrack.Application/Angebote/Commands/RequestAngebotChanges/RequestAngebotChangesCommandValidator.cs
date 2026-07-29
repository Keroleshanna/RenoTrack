using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;

public sealed class RequestAngebotChangesCommandValidator : AbstractValidator<RequestAngebotChangesCommand>
{
    public RequestAngebotChangesCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.Comment).NotEmpty();
        RuleFor(c => c.ReviewedByAdminId).GreaterThan(0);
    }
}
