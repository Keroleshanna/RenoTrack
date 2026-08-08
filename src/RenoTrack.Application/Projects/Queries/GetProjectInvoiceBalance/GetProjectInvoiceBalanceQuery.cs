using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects.Dtos;

namespace RenoTrack.Application.Projects.Queries.GetProjectInvoiceBalance;

/// <summary>
/// BR-3's running total for one Project (Sequence Diagram §8, Wireframes E1/E2): what was agreed,
/// what has been invoiced so far, and the difference.
///
/// <para>
/// <b>No <c>RequestingInspectorId</c>.</b> `PermissionMatrix.md` §5 grants this Admin <c>F</c> /
/// Inspector <c>R</c> — read-only but **unscoped**, the same grant as the Project detail read. A
/// scope parameter would invent a per-Inspector restriction the matrix does not state, and a
/// reflection test pins the single-parameter shape so reintroducing one would be a visible
/// signature change reviewed against §5, not a predicate quietly added to a <c>WHERE</c> clause.
/// </para>
/// </summary>
public sealed record GetProjectInvoiceBalanceQuery(int ProjectId);

public sealed class GetProjectInvoiceBalanceQueryValidator : AbstractValidator<GetProjectInvoiceBalanceQuery>
{
    public GetProjectInvoiceBalanceQueryValidator()
    {
        RuleFor(q => q.ProjectId).GreaterThan(0);
    }
}

/// <summary>
/// Reads through <see cref="IProjectQueries"/> — a projection, not aggregate hydration (D36). The
/// answer spans two tables and loading a Project plus every one of its Invoices to sum one column
/// would be work with no purpose.
///
/// <para>
/// <b>The handler applies no policy of its own.</b> It does not clamp <c>Remaining</c>, does not
/// flag over-invoicing, and does not reject anything: BR-3's warning *is* the number, and a negative
/// value is the signal. Anything else here would turn a documented warning into behaviour no
/// document describes.
/// </para>
/// </summary>
public sealed class GetProjectInvoiceBalanceQueryHandler(
    IValidator<GetProjectInvoiceBalanceQuery> validator,
    IProjectQueries projectQueries) : IQueryHandler<GetProjectInvoiceBalanceQuery, ProjectInvoiceBalanceDto>
{
    public async Task<ProjectInvoiceBalanceDto> HandleAsync(
        GetProjectInvoiceBalanceQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await projectQueries.GetInvoiceBalanceAsync(query.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.Project), query.ProjectId);
    }
}
