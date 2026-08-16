using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Records the arguments it was called with. What matters for the Invoice reads is that the filters
/// and the caller-supplied "as of" date reach the query unchanged — the arithmetic itself is SQL and
/// is covered against a real database in <c>RenoTrack.Infrastructure.Tests</c>, never here.
/// </summary>
public sealed class FakeInvoiceQueries : IInvoiceQueries
{
    public List<(InvoiceStatus? Status, int? ProjectId, DateTime? DueBefore, int Page, int PageSize)> PagedCalls { get; } = [];

    public PagedResult<InvoiceListItemDto> PagedResult { get; set; } = new([], 1, 25, 0);

    public Task<PagedResult<InvoiceListItemDto>> GetPagedAsync(
        InvoiceStatus? status,
        int? projectId,
        DateTime? dueBefore,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        PagedCalls.Add((status, projectId, dueBefore, page, pageSize));
        return Task.FromResult(PagedResult);
    }

    public List<DateTime> ReceivablesCalls { get; } = [];

    public ReceivablesSummaryDto Receivables { get; set; } = new(0m, 0m, 0m, 0m, 0m, 0, 0, 0);

    public Task<ReceivablesSummaryDto> GetReceivablesAsync(DateTime asOf, CancellationToken cancellationToken)
    {
        ReceivablesCalls.Add(asOf);
        return Task.FromResult(Receivables);
    }
}
