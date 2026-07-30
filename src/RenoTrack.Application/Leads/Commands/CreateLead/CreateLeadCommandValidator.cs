using FluentValidation;

namespace RenoTrack.Application.Leads.Commands.CreateLead;

/// <summary>
/// Shape validation only (Architecture.md §12) — required fields and a plausible email
/// format. Domain invariants (e.g. Lead.Create's own non-empty checks) are the backstop, not
/// the primary path; this validator exists to give the caller a friendly, field-level 400
/// before the Domain is even touched.
/// </summary>
public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Phone).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Source).IsInEnum();
    }
}
