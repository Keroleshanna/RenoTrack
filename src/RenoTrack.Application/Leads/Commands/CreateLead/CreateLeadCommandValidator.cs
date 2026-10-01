using FluentValidation;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Leads.Commands.CreateLead;

/// <summary>
/// Shape validation only (Architecture.md §12) — required fields, a plausible email format, and
/// lengths that fit the schema. Domain invariants (e.g. Lead.Create's own non-empty checks) are the
/// backstop, not the primary path; this validator exists to give the caller a friendly, field-level
/// 400 before the Domain is even touched.
/// </summary>
/// <remarks>
/// <b>The maximum lengths are <see cref="Lead"/>'s own constants, never repeated literals</b>
/// (Phase 13 Slice 7). Without them an over-long value reached the database and surfaced as a 500
/// with a stack trace — an ordinary bad request answered as a server fault. The limits bind on both
/// creation paths, the anonymous contact form and the Admin's manual entry, because the defect was
/// the same on both.
/// </remarks>
public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Lead.MaxNameLength);
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(Lead.MaxPhoneLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(Lead.MaxEmailLength);
        RuleFor(c => c.Address).MaximumLength(Lead.MaxAddressLength);
        RuleFor(c => c.Notes).MaximumLength(Lead.MaxNotesLength);
        RuleFor(c => c.Source).IsInEnum();
    }
}
