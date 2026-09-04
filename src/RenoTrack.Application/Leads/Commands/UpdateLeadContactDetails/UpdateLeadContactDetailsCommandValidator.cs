using FluentValidation;

namespace RenoTrack.Application.Leads.Commands.UpdateLeadContactDetails;

/// <summary>
/// Shape validation only (CLAUDE.md §5) — the same three required fields and email format
/// <c>CreateLeadCommandValidator</c> enforces, because the corrected Lead must satisfy exactly the
/// invariants the created one did. <c>Lead.UpdateContactDetails</c>'s own guards remain the
/// backstop; this exists to return a friendly field-keyed 400 before the Domain is touched.
/// </summary>
/// <remarks>
/// No rule on <c>RequestingInspectorId</c>: <c>null</c> is its meaningful Admin value, and the
/// ownership decision belongs to the handler, not here (a validator never encodes authorization).
/// <c>Address</c> is optional at the Domain level and stays optional here.
/// </remarks>
public sealed class UpdateLeadContactDetailsCommandValidator
    : AbstractValidator<UpdateLeadContactDetailsCommand>
{
    public UpdateLeadContactDetailsCommandValidator()
    {
        RuleFor(c => c.LeadId).GreaterThan(0);
        RuleFor(c => c.PerformedByUserId).GreaterThan(0);
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Phone).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
    }
}
