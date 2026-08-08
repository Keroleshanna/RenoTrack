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
}
