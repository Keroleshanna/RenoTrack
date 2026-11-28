using FluentValidation;

namespace RenoTrack.Application.Angebote.Commands.ReturnAngebotForCorrection;

/// <summary>Shape only (CLAUDE.md §5). "Only while InReview" is the aggregate's guard.</summary>
public sealed class ReturnAngebotForCorrectionCommandValidator : AbstractValidator<ReturnAngebotForCorrectionCommand>
{
    public ReturnAngebotForCorrectionCommandValidator()
    {
        RuleFor(c => c.AngebotId).GreaterThan(0);
        RuleFor(c => c.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PerformedByAdminId).GreaterThan(0);
    }
}
