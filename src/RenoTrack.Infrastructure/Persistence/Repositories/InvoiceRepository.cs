using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// <c>AddAsync</c> only, matching the interface exactly. Adding an Invoice also stages its
/// <c>Payments</c> — there are none at creation, and nothing in this slice creates one.
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
}
