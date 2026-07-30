using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lead has no child entities and no navigation properties (Architecture.md §6 — it relates to
/// every other aggregate by id only), so GetByIdAsync needs no Include/ThenInclude — a plain
/// FindAsync PK lookup already returns the full aggregate. FindAsync's tracked result is relied
/// upon by every handler that loads-then-mutates a Lead (e.g. CompleteInspectionCommandHandler),
/// since no UpdateAsync method exists anywhere in this project (CLAUDE.md §4) — persistence of a
/// mutation happens exclusively through EF Core's change tracker plus IUnitOfWork.SaveChangesAsync.
/// </summary>
public sealed class LeadRepository(RenoTrackDbContext dbContext) : ILeadRepository
{
    public async Task AddAsync(Lead lead, CancellationToken cancellationToken) =>
        await dbContext.Leads.AddAsync(lead, cancellationToken);

    public async Task<Lead?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Leads.FindAsync([id], cancellationToken);
}
