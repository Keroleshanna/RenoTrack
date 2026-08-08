using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the Invoice aggregate. Two methods, each with a named consumer:
/// <c>AddAsync</c> for <c>CreateInvoiceCommand</c> (Slice 3) and <c>GetByIdAsync</c> for
/// <c>SendInvoiceCommand</c> (Slice 4). Mark-paid and void arrive in Slice 5 and will add exactly
/// what they need then (CLAUDE.md §4).
/// </summary>
public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    /// <summary>
    /// Loads an Invoice with its full aggregate — the Payments collection included (CLAUDE.md §4:
    /// there is no partial-load contract for an aggregate root). Added in Phase 8 Slice 4, when
    /// <c>SendInvoiceCommand</c> first needed to load and mutate an existing Invoice.
    /// </summary>
    Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
