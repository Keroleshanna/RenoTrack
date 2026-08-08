using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the Invoice aggregate. <c>AddAsync</c> only, because
/// <c>CreateInvoiceCommand</c> is the only command that exists — every other Invoice use case
/// (send, mark paid, void) arrives in a later slice and will add exactly what it needs then
/// (CLAUDE.md §4).
/// </summary>
public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);
}
