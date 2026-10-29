using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;

public sealed class SubmitAngebotForReviewCommandValidator : AbstractValidator<SubmitAngebotForReviewCommand>
{
    public SubmitAngebotForReviewCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(c => c.RequestingInspectorId).GreaterThan(0).When(c => c.RequestingInspectorId is not null);
        RuleFor(c => c.PerformedByUserId).GreaterThan(0);
    }
}
