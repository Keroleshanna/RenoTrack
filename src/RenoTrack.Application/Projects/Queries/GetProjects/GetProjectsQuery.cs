using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Queries.GetProjects;

/// <summary>
/// The Project list. Admin "F", Inspector "R" — and **unscoped for both**.
/// </summary>
/// <remarks>
/// Before Phase 10 a Project was reachable only by id, so the list was unreachable without already
/// knowing every id — a gap `PHASE10_PROGRESS.md` recorded before any of this was built.
/// </remarks>
public sealed record GetProjectsQuery(
    ProjectStatus? Status,
    int Page = Pagination.FirstPage,
    int PageSize = Pagination.DefaultPageSize);

public sealed class GetProjectsQueryValidator : AbstractValidator<GetProjectsQuery>
{
    public GetProjectsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Pagination.FirstPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Pagination.MaxPageSize);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
    }
}

/// <remarks>
/// <b>No scope parameter anywhere, and that is correct.</b> `PermissionMatrix.md` §5 grants
/// "View Project detail" as Admin "F" / Inspector "R", explicitly **unscoped** — an Inspector may
/// read any Project, including ones they never worked, and that read confers no Invoice permission
/// whatsoever. Adding an ownership filter here would invent a restriction the matrix does not have,
/// which is as much a defect as omitting one it does (CLAUDE.md §16).
/// </remarks>
public sealed class GetProjectsQueryHandler(
    IValidator<GetProjectsQuery> validator,
    IProjectQueries projectQueries) : IQueryHandler<GetProjectsQuery, PagedResult<ProjectListItemDto>>
{
    public async Task<PagedResult<ProjectListItemDto>> HandleAsync(
        GetProjectsQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await projectQueries.GetPagedAsync(
            query.Status,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
