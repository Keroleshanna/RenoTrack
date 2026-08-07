using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects.Dtos;

namespace RenoTrack.Application.Projects.Queries.GetProjectById;

/// <summary>
/// Reads one Project with its originating context (SRS FR-7.4, Wireframe E1).
///
/// <para>
/// <b>No <c>RequestingInspectorId</c>, unlike <c>GetLeadByIdQuery</c>.</b> That query carries one
/// because `PermissionMatrix.md` §1 scopes a Lead to its assigned Inspector (<c>S</c>). §5 marks
/// "View Project detail" Admin <c>F</c> / Inspector <c>R</c> — read-only but unscoped — so there is
/// no ownership rule to enforce and nothing for the API layer to supply. Adding the parameter would
/// invent a restriction the documents do not state (CLAUDE.md §16).
/// </para>
/// </summary>
public sealed record GetProjectByIdQuery(int Id);

public sealed class GetProjectByIdQueryValidator : AbstractValidator<GetProjectByIdQuery>
{
    public GetProjectByIdQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

/// <summary>
/// Reads through <see cref="IProjectQueries"/> — a DTO projection — rather than through
/// <c>IProjectRepository</c>. The opposite choice from <c>GetLeadByIdQueryHandler</c>, and for a
/// stated reason: that handler needs the Domain entity in hand so <c>IOwnershipValidator</c> can
/// judge it. Here no ownership rule applies at all, and the response spans three tables
/// (Projects, Customers, Angebote), so hydrating aggregates to then read four fields off them
/// would be work with no purpose (D36).
/// </summary>
public sealed class GetProjectByIdQueryHandler(
    IValidator<GetProjectByIdQuery> validator,
    IProjectQueries projectQueries) : IQueryHandler<GetProjectByIdQuery, ProjectDetailDto>
{
    public async Task<ProjectDetailDto> HandleAsync(GetProjectByIdQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await projectQueries.GetByIdAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Project), query.Id);
    }
}
