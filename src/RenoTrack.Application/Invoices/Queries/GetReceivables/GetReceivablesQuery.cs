using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices.Dtos;

namespace RenoTrack.Application.Invoices.Queries.GetReceivables;

/// <summary>
/// The company's receivables position — invoiced, paid, open, overdue. Admin only (§5).
/// </summary>
/// <param name="AsOf">
/// The date "overdue" is judged against. **Taken from the caller rather than the server clock**, so
/// the figure is reproducible, testable, and identical to whatever the client computed locally.
/// </param>
public sealed record GetReceivablesQuery(DateTime AsOf);

public sealed class GetReceivablesQueryValidator : AbstractValidator<GetReceivablesQuery>
{
    public GetReceivablesQueryValidator()
    {
        RuleFor(q => q.AsOf).NotEqual(default(DateTime));
    }
}

/// <remarks>
/// Aggregated in SQL by the query implementation, never by summing a page in memory — a total that
/// silently described one page while being labelled as the company's outstanding money is exactly
/// the wrong number a cockpit exists to prevent.
/// </remarks>
public sealed class GetReceivablesQueryHandler(
    IValidator<GetReceivablesQuery> validator,
    IInvoiceQueries invoiceQueries) : IQueryHandler<GetReceivablesQuery, ReceivablesSummaryDto>
{
    public async Task<ReceivablesSummaryDto> HandleAsync(
        GetReceivablesQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        return await invoiceQueries.GetReceivablesAsync(query.AsOf, cancellationToken);
    }
}
