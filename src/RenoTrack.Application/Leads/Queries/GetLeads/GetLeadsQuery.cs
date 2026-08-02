using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Leads.Queries.GetLeads;

/// <summary>
/// The Lead pipeline (SRS FR-2.4, Wireframe B2).
/// </summary>
/// <param name="AssignedInspectorId">
/// For an Admin, the optional filter they chose. For an Inspector, their own id — forced by the API
/// layer regardless of what the caller sent, which is how <c>PermissionMatrix.md</c> §1's
/// "filtered server-side" scoping is implemented (D61). This handler cannot distinguish the two
/// cases and does not need to.
/// </param>
public sealed record GetLeadsQuery(
    LeadStatus? Status,
    int? AssignedInspectorId,
    DateTime? CreatedFrom,
    DateTime? CreatedTo,
    int Page = Pagination.FirstPage,
    int PageSize = Pagination.DefaultPageSize);

/// <summary>
/// Shape only (CLAUDE.md §5) — paging bounds and a coherent date range. Nothing here queries
/// anything, and no filter value is checked for business meaning (a status with no matching Leads
/// is an empty page, not an error).
/// </summary>
public sealed class GetLeadsQueryValidator : AbstractValidator<GetLeadsQuery>
{
    public GetLeadsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Pagination.FirstPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Pagination.MaxPageSize);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);

        RuleFor(q => q.CreatedFrom)
            .LessThanOrEqualTo(q => q.CreatedTo)
            .When(q => q.CreatedFrom.HasValue && q.CreatedTo.HasValue)
            .WithMessage("'Created From' must not be after 'Created To'.");
    }
}

/// <summary>
/// Deliberately thin: every filter, the paging arithmetic, and the total-count query live in
/// whatever implements <see cref="ILeadQueries"/>, matching how
/// <c>SearchCatalogItemsQueryHandler</c> delegates entirely to <c>ICatalogItemQueries</c>.
/// </summary>
/// <remarks>
/// No <c>IOwnershipValidator</c> here, and its absence is correct rather than an oversight: a
/// collection cannot be ownership-checked after the fact without loading every Lead first. Scoping
/// is a <c>WHERE</c> clause, applied through <c>AssignedInspectorId</c> — see that parameter's note.
/// </remarks>
public sealed class GetLeadsQueryHandler(
    IValidator<GetLeadsQuery> validator,
    ILeadQueries leadQueries) : IQueryHandler<GetLeadsQuery, PagedResult<LeadDto>>
{
    public async Task<PagedResult<LeadDto>> HandleAsync(GetLeadsQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await leadQueries.GetPagedAsync(
            query.Status,
            query.AssignedInspectorId,
            query.CreatedFrom,
            query.CreatedTo,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
