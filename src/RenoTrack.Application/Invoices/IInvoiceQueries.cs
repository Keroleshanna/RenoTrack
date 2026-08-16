using RenoTrack.Application.Common;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices;

/// <summary>
/// Read-side access to Invoices, bypassing aggregate hydration (D36).
/// </summary>
/// <remarks>
/// <para>
/// New in Phase 10. Before it, the only Invoice read in the system was
/// <c>GET /api/v1/public/invoices/{token}</c> — anonymous, single-invoice and token-gated — so the
/// company had no way to see its own receivables at all.
/// </para>
/// <para>
/// Lives in the <c>Invoices</c> feature folder rather than <c>Common.Interfaces</c>, because its
/// return types are feature DTOs and <c>Common</c> must never depend on a feature folder (D23) —
/// the same placement as <c>ILeadQueries</c> and <c>IAngebotQueries</c>.
/// </para>
/// <para>
/// <b>No scope parameter anywhere.</b> <c>PermissionMatrix.md</c> §5 makes every Invoice action
/// Admin-"F", so there is no owning Inspector to scope to. The API layer enforces the role; adding
/// a scope argument here would imply a rule the matrix does not have (CLAUDE.md §16).
/// </para>
/// </remarks>
public interface IInvoiceQueries
{
    /// <summary>
    /// Invoices, newest first, optionally filtered by status and by a due-date window.
    /// </summary>
    /// <param name="dueBefore">
    /// Upper bound on <c>DueDate</c>, used for "due soon" and "overdue" reads. Applied to the date
    /// only, so the caller decides what "today" means rather than the query assuming a server clock.
    /// </param>
    Task<PagedResult<InvoiceListItemDto>> GetPagedAsync(
        InvoiceStatus? status,
        int? projectId,
        DateTime? dueBefore,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// The receivables position across every Invoice, aggregated in SQL.
    /// </summary>
    /// <param name="asOf">
    /// The date "overdue" is judged against. Supplied by the caller rather than read from the server
    /// clock, so the figure is reproducible and testable — and so a client in a different timezone
    /// cannot be told a different number than the one it computed locally.
    /// </param>
    Task<ReceivablesSummaryDto> GetReceivablesAsync(DateTime asOf, CancellationToken cancellationToken);
}
