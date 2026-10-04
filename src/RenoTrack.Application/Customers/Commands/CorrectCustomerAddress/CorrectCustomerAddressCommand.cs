using FluentValidation;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Customers.Commands.CorrectCustomerAddress;

/// <summary>
/// Sets or corrects a Customer's address (BR-5, SRS FR-7.5, <c>PermissionMatrix.md</c> §5
/// "Correct customer address — Admin F", Phase 14 Slice 2b, D112).
/// </summary>
/// <param name="CorrectedByAdminId">The acting Admin, from the JWT, for the audit entry (D61).</param>
/// <remarks>
/// <b>The address is the only field.</b> Name, email and phone are not correctable by decision
/// (D112), so the command cannot express them — a request that sends them changes nothing.
/// </remarks>
public sealed record CorrectCustomerAddressCommand(int CustomerId, string Address, int CorrectedByAdminId);

/// <summary>
/// Shape only (CLAUDE.md §5): a present address within <see cref="Customer.MaxAddressLength"/>.
/// <c>Customer.CorrectAddress</c> is the invariant's real home and its backstop; this exists to give
/// a field-keyed 400 before the aggregate is loaded.
/// </summary>
/// <remarks>
/// <c>NotEmpty</c> refuses whitespace-only input as well as empty input. The maximum is measured on
/// the raw value, exactly as the D109 validators measure theirs; the Domain measures after trimming.
/// </remarks>
public sealed class CorrectCustomerAddressCommandValidator : AbstractValidator<CorrectCustomerAddressCommand>
{
    public CorrectCustomerAddressCommandValidator()
    {
        RuleFor(c => c.CustomerId).GreaterThan(0);
        RuleFor(c => c.CorrectedByAdminId).GreaterThan(0);
        RuleFor(c => c.Address).NotEmpty().MaximumLength(Customer.MaxAddressLength);
    }
}
