using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;

public sealed class SubmitAngebotForReviewCommandValidator : AbstractValidator<SubmitAngebotForReviewCommand>
{
    public SubmitAngebotForReviewCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.InspectorId).GreaterThan(0);
    }
}
