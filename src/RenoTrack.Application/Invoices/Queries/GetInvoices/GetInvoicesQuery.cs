using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Queries.GetInvoices;

/// <summary>
/// The Invoice list (Phase 10's Rechnungen workspace). Admin only — PermissionMatrix.md §5.
/// </summary>
public sealed record GetInvoicesQuery(
    InvoiceStatus? Status,
    int? ProjectId,
    DateTime? DueBefore,
    int Page = Pagination.FirstPage,
    int PageSize = Pagination.DefaultPageSize);

/// <summary>Shape only (CLAUDE.md §5).</summary>
public sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    public GetInvoicesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Pagination.FirstPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Pagination.MaxPageSize);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
        RuleFor(q => q.ProjectId).GreaterThan(0).When(q => q.ProjectId.HasValue);
    }
}

/// <remarks>
/// No ownership check and no scope parameter: PermissionMatrix.md §5 marks every Invoice action
/// Admin-"F", enforced by the controller's role attribute. Using <c>IOwnershipValidator</c> here
/// would be the semantic error CLAUDE.md §16 describes, not merely redundant code.
/// </remarks>
public sealed class GetInvoicesQueryHandler(
    IValidator<GetInvoicesQuery> validator,
    IInvoiceQueries invoiceQueries) : IQueryHandler<GetInvoicesQuery, PagedResult<InvoiceListItemDto>>
{
    public async Task<PagedResult<InvoiceListItemDto>> HandleAsync(
        GetInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await invoiceQueries.GetPagedAsync(
            query.Status,
            query.ProjectId,
            query.DueBefore,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
