using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Unlike LeadRepository, Inspection has one child collection (Photos), so GetByIdAsync must
/// Include it — CLAUDE.md §4: "a repository loaded via GetByIdAsync always returns the full
/// aggregate." FindAsync doesn't support Include, so a plain FirstOrDefaultAsync on the PK is
/// used instead. The tracked result is what lets UploadInspectionPhotoCommandHandler's
/// inspection.AddPhoto(...) be picked up by SaveChangesAsync with no UpdateAsync method — same
/// reliance on EF's change-tracking graph walk as LeadRepository.
/// </summary>
public sealed class InspectionRepository(RenoTrackDbContext dbContext) : IInspectionRepository
{
    public async Task AddAsync(Inspection inspection, CancellationToken cancellationToken) =>
        await dbContext.Inspections.AddAsync(inspection, cancellationToken);

    public async Task<Inspection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Inspections
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
}
