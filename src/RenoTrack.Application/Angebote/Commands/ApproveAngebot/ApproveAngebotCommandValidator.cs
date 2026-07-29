using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.ApproveAngebot;

public sealed class ApproveAngebotCommandValidator : AbstractValidator<ApproveAngebotCommand>
{
    public ApproveAngebotCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ReviewedByAdminId).GreaterThan(0);
    }
}
