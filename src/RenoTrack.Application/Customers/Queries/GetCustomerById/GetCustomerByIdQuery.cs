using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Customers.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Customers.Queries.GetCustomerById;

/// <summary>
/// Reads one Customer for the Admin's address panel (<c>PermissionMatrix.md</c> §5 "View customer
/// address — Admin F", Phase 14 Slice 2b, D112).
/// </summary>
/// <remarks>
/// <b>Not a Customers list, and not on the Project detail read.</b> D91's refusal of a Customers
/// workspace and list still stands. The address is read here, behind an Admin-only route, rather than
/// added to <c>ProjectDetailDto</c>, because that read is Inspector-readable and unscoped — adding the
/// address there would show every customer's address to every Inspector, which no document asks for.
/// </remarks>
public sealed record GetCustomerByIdQuery(int Id);

public sealed class GetCustomerByIdQueryValidator : AbstractValidator<GetCustomerByIdQuery>
{
    public GetCustomerByIdQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

/// <summary>
/// Reads through <see cref="ICustomerRepository"/> rather than a projection, the single-resource
/// shape <c>GetLeadByIdQueryHandler</c> uses (CLAUDE.md §22): Customer owns no children, so this is
/// one <c>FindAsync</c>. Admin <c>F</c>, so no ownership check (CLAUDE.md §16).
/// </summary>
public sealed class GetCustomerByIdQueryHandler(
    IValidator<GetCustomerByIdQuery> validator,
    ICustomerRepository customerRepository) : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<CustomerDto> HandleAsync(GetCustomerByIdQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var customer = await customerRepository.GetByIdAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), query.Id);

        return customer.ToDto();
    }
}
