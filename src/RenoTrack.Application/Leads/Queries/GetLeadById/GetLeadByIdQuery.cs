using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Leads.Dtos;
using FluentValidation;

namespace RenoTrack.Application.Leads.Queries.GetLeadById;

/// <summary>
/// Reads one Lead (Wireframe C1's detail page).
/// </summary>
/// <param name="RequestingInspectorId">
/// The Inspector the caller is, or <c>null</c> when the caller is an Admin. Not "the caller's user
/// id": an Admin has full ("F") access per <c>PermissionMatrix.md</c> §1 and is subject to no
/// ownership rule, so passing their id would invite a check that must not happen. Supplied by the
/// API layer from the JWT, never from the request (D61).
/// </param>
public sealed record GetLeadByIdQuery(int Id, int? RequestingInspectorId);

public sealed class GetLeadByIdQueryValidator : AbstractValidator<GetLeadByIdQuery>
{
    public GetLeadByIdQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

/// <summary>
/// Loads the aggregate through <c>ILeadRepository</c> rather than a DTO projection, so ownership
/// stays centralized in <see cref="IOwnershipValidator"/> (CLAUDE.md §9, §16) instead of being
/// re-expressed as an inline comparison or a second SQL predicate. Costs nothing extra: Lead owns
/// no children, so this is one <c>FindAsync</c>.
/// </summary>
public sealed class GetLeadByIdQueryHandler(
    IValidator<GetLeadByIdQuery> validator,
    ILeadRepository leadRepository,
    IOwnershipValidator ownershipValidator) : IQueryHandler<GetLeadByIdQuery, LeadDto>
{
    public async Task<LeadDto> HandleAsync(GetLeadByIdQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var lead = await leadRepository.GetByIdAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Lead), query.Id);

        // Only an Inspector is scoped ("S"); an Admin has full access ("F") and no ownership rule
        // applies at all — calling the validator for them would be a semantic error, not merely
        // redundant (CLAUDE.md §16).
        if (query.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureLeadOwnership(lead, inspectorId);
        }

        return lead.ToDto();
    }
}
