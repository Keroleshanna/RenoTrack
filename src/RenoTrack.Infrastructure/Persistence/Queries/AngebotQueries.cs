using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Infrastructure.Persistence.Queries;

public sealed class AngebotQueries(RenoTrackDbContext dbContext) : IAngebotQueries
{
    public async Task<IReadOnlyList<AngebotDto>> GetForLeadAsync(
        int leadId,
        int? requestingInspectorId,
        CancellationToken cancellationToken) =>
        await dbContext.Angebote
            .AsNoTracking()
            .Where(a => a.LeadId == leadId)

            // The Inspector's scope, applied in SQL rather than after loading (CLAUDE.md §22).
            // Null means Admin, who is "F" and sees every Angebot on the Lead.
            .Where(a => requestingInspectorId == null || a.CreatedByInspectorId == requestingInspectorId)

            // Newest first, with Id as the tiebreaker: CreatedAt is not unique, and two Angebote
            // created in the same tick would otherwise be free to swap places between reads.
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new AngebotDto(
                a.Id,
                a.LeadId,
                a.InspectionId,
                a.AngebotNumber,
                a.Status,
                a.CreatedByInspectorId,
                a.ReviewedByAdminId,
                a.SentAt,
                a.DecisionAt,
                a.CreatedAt,

                // The stored columns, not a re-derivation: NetTotal/GrossTotal are the two fields
                // ERD.md caches precisely so a list page need not walk the item tree (D15).
                a.NetTotal.Amount,
                a.GrossTotal.Amount))
            .ToListAsync(cancellationToken);
}
