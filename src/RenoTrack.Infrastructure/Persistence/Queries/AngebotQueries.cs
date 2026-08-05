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

    /// <summary>
    /// Reaches the item through its Angebot's navigations, since <c>AngebotItem</c> has no
    /// <c>DbSet</c> of its own — child entities are only ever reachable through their aggregate root
    /// (CLAUDE.md §21), and that rule holds for reads as well as writes.
    /// </summary>
    public async Task<ItemDto?> GetItemAsync(int itemId, CancellationToken cancellationToken) =>
        await dbContext.Angebote
            .AsNoTracking()
            .SelectMany(a => a.Sections)
            .SelectMany(s => s.Items)
            .Where(i => i.Id == itemId)
            .Select(i => new ItemDto(
                i.Id,
                i.CatalogItemId,
                i.Description,
                i.Specification,
                i.Quantity,
                i.Unit.Code,
                i.UnitPrice.Amount,
                i.VatRate,

                // LineTotal is a computed Domain property with no column, so the projection
                // restates Architecture.md §6.1 step 1 rather than reading it.
                i.Quantity * i.UnitPrice.Amount))
            .SingleOrDefaultAsync(cancellationToken);
}
