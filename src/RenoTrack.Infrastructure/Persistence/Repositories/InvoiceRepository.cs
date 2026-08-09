using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Matches <c>IInvoiceRepository</c> exactly, method for method. Adding an Invoice also stages its
/// <c>Payments</c> — there are none at creation, and only <c>MarkPaid</c> ever adds one.
///
/// <para>
/// Performs no validation (the Domain guards its own invariants) and never calls
/// <c>SaveChangesAsync</c> — that stays exclusively <c>IUnitOfWork</c>'s job.
/// </para>
/// </summary>
public sealed class InvoiceRepository(RenoTrackDbContext dbContext) : IInvoiceRepository
{
    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken) =>
        await dbContext.Invoices.AddAsync(invoice, cancellationToken);

    /// <summary>
    /// Eagerly includes <c>Payments</c> — CLAUDE.md §4's "a repository returns the full aggregate"
    /// rule, which is why this uses <c>FirstOrDefaultAsync</c> rather than <c>FindAsync</c>
    /// (<c>FindAsync</c> supports no <c>Include</c>), exactly as <c>InspectionRepository</c> does
    /// for its photos. Nothing in Slice 4 reads a Payment, but a partial load is not a contract
    /// this project offers.
    /// </summary>
    public async Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .Include(invoice => invoice.Payments)
            .FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    /// <summary>
    /// Two existence checks rather than one loaded list — the interface's own documentation states
    /// the rule; this only answers it.
    ///
    /// <para>
    /// Both run entirely in SQL and both are backed by <c>IX_Invoices_ProjectId</c>, so the cost is
    /// two indexed existence probes, never a scan and never a materialised row. The second is
    /// skipped whenever the first already settles the answer.
    /// </para>
    /// <para>
    /// <b>Status is compared, never summed</b>, which keeps this clear of the constraint Slice 3
    /// hit: a value-converted <c>Money</c> does not translate inside an aggregate. Comparing the
    /// string-stored <see cref="InvoiceStatus"/> in a <c>WHERE</c> is the same shape
    /// <c>ProjectQueries.GetInvoiceBalanceAsync</c> already relies on.
    /// </para>
    /// </summary>
    public async Task<bool> HasCompletionBlockingInvoicesForProjectAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        var invoices = dbContext.Invoices.AsNoTracking().Where(invoice => invoice.ProjectId == projectId);

        // Clause 1 — a Project that was never invoiced is blocked (Phase 8 Slice 6, decision I-2).
        if (!await invoices.AnyAsync(cancellationToken))
            return true;

        // Clause 2 — anything still owed or unsent blocks; Paid and Void never do (decision K-1).
        return await invoices.AnyAsync(
            invoice => invoice.Status == InvoiceStatus.Draft
                       || invoice.Status == InvoiceStatus.Sent
                       || invoice.Status == InvoiceStatus.Overdue,
            cancellationToken);
    }
}
