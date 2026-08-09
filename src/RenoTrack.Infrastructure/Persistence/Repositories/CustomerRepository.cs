using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Customer owns no children and holds no navigation properties, so neither method needs an
/// Include — the same shape as LeadRepository.
///
/// <para>
/// <c>FindByLeadIdAsync</c> is a tracked query on purpose. The conversion handler passes the
/// returned instance's <c>Id</c> into <c>Project.Create</c>, and a future caller that mutated a
/// Customer would depend on the change tracker seeing it, since no <c>UpdateAsync</c> exists
/// anywhere in this project. <c>SingleOrDefaultAsync</c> rather than <c>FirstOrDefault</c>:
/// <c>Customers.LeadId</c> is unique, so a second row is a corrupted database and should throw
/// rather than be silently picked between.
/// </para>
/// </summary>
public sealed class CustomerRepository(RenoTrackDbContext dbContext) : ICustomerRepository
{
    public async Task AddAsync(Customer customer, CancellationToken cancellationToken) =>
        await dbContext.Customers.AddAsync(customer, cancellationToken);

    public async Task<Customer?> FindByLeadIdAsync(int leadId, CancellationToken cancellationToken) =>
        await dbContext.Customers.SingleOrDefaultAsync(c => c.LeadId == leadId, cancellationToken);

    /// <summary>
    /// <c>FindAsync</c> — a primary-key lookup with nothing to <c>Include</c>, matching
    /// <c>LeadRepository</c> and <c>ProjectRepository</c>.
    /// </summary>
    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Customers.FindAsync([id], cancellationToken);
}
