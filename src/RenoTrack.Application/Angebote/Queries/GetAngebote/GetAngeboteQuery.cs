using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Queries.GetAngebote;

/// <summary>
/// Every Angebot in the system, filterable and paged — the read behind the Angebote workspace and
/// the Cockpit's "waiting for approval" / "with the customer" figures.
/// </summary>
/// <remarks>
/// Until now Angebote were readable **only through their Lead**
/// (<c>GET /api/v1/leads/{leadId}/angebote</c>), which cannot answer "which quotes are in review"
/// without one request per Lead. This is that missing read.
/// </remarks>
/// <param name="RequestingInspectorId">
/// The caller's own id when they are an Inspector, <see langword="null"/> for an Admin. Set by the
/// API layer from the token, never from the query string (D61) — an Inspector cannot widen their own
/// visibility by asking, exactly as <c>GetLeadsQuery</c> works. <c>PermissionMatrix.md</c> §3–4 marks
/// the Inspector "S" and the Admin "F".
/// </param>
public sealed record GetAngeboteQuery(
    AngebotStatus? Status,
    int? RequestingInspectorId,
    int Page = Pagination.FirstPage,
    int PageSize = Pagination.DefaultPageSize);

/// <summary>Shape only (CLAUDE.md §5): paging bounds and a real enum value.</summary>
public sealed class GetAngeboteQueryValidator : AbstractValidator<GetAngeboteQuery>
{
    public GetAngeboteQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Pagination.FirstPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Pagination.MaxPageSize);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
    }
}

/// <summary>
/// Thin by design — every filter, the join to Lead and the paging arithmetic live in
/// <see cref="IAngebotQueries"/>, matching <c>GetLeadsQueryHandler</c>.
/// </summary>
/// <remarks>
/// <b>No <c>IOwnershipValidator</c>, and that is correct rather than an omission.</b> A collection
/// cannot be ownership-checked after loading without loading everything first; scoping a list is a
/// <c>WHERE</c> clause, applied through <see cref="GetAngeboteQuery.RequestingInspectorId"/>. This is
/// the same split CLAUDE.md §22 records: single-resource reads use the validator, collection reads
/// scope in SQL.
/// </remarks>
public sealed class GetAngeboteQueryHandler(
    IValidator<GetAngeboteQuery> validator,
    IAngebotQueries angebotQueries) : IQueryHandler<GetAngeboteQuery, PagedResult<AngebotListItemDto>>
{
    public async Task<PagedResult<AngebotListItemDto>> HandleAsync(
        GetAngeboteQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await angebotQueries.GetPagedAsync(
            query.Status,
            query.RequestingInspectorId,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
