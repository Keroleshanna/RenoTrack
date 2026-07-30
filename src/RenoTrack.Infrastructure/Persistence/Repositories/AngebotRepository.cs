using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Angebot has a two-level child tree (Sections -> Items), both encapsulated collections over
/// private backing fields (same PropertyAccessMode.Field pattern as Inspection.Photos).
/// GetByIdAsync always loads the full tree via a single Include/ThenInclude chain — CLAUDE.md
/// §4's "no partial load of an aggregate root" contract, needed for real by
/// Angebot.SubmitForReview()'s own self-guard, which inspects Sections/Items directly.
/// AsSplitQuery is deliberately not used: Sections/Items is a single chain, not sibling
/// collections, so there is no cartesian-product row inflation to split for.
///
/// HasActiveAngebotForLeadAsync is a pure existence check (StateMachine.md §2.4: only one
/// non-terminal Angebot per Lead) — no Include, touches only Angebote's own columns.
/// </summary>
public sealed class AngebotRepository(RenoTrackDbContext dbContext) : IAngebotRepository
{
    public async Task AddAsync(Angebot angebot, CancellationToken cancellationToken) =>
        await dbContext.Angebote.AddAsync(angebot, cancellationToken);

    public async Task<Angebot?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Angebote
            .Include(a => a.Sections)
            .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<bool> HasActiveAngebotForLeadAsync(int leadId, CancellationToken cancellationToken) =>
        await dbContext.Angebote.AnyAsync(
            a => a.LeadId == leadId
                && a.Status != AngebotStatus.CustomerApproved
                && a.Status != AngebotStatus.CustomerRejected,
            cancellationToken);
}
