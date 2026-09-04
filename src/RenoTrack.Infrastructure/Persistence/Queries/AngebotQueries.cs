using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Enums;

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
                i.Quantity * i.UnitPrice.Amount,

                // Always false here, and deliberately not looked up: this read serves
                // SaveAngebotItemAsCatalogItem, which asks the repository that question itself and
                // needs the entity, not a flag. Only the document read populates it.
                false))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The customer's name comes from a <b>join to Leads inside the projection</b>. The write model
    /// relates the two by id only and still does — nothing here gives <c>Angebot</c> a navigation
    /// property. This is precisely the freedom a read-side interface buys (D36): the query may shape
    /// data across aggregates as long as the aggregates themselves stay independent.
    /// </para>
    /// <para>
    /// A <c>join</c> rather than a correlated sub-select, so SQL Server sees one <c>INNER JOIN</c>
    /// instead of a per-row lookup. The relationship is required (<c>Angebot.LeadId</c> is non-null
    /// with a real FK, Phase 3), so an inner join can never drop a row.
    /// </para>
    /// </remarks>
    public async Task<PagedResult<AngebotListItemDto>> GetPagedAsync(
        AngebotStatus? status,
        int? requestingInspectorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Angebote.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        // Null means Admin — "F" in PermissionMatrix.md §3–4, so no restriction at all.
        if (requestingInspectorId.HasValue)
        {
            query = query.Where(a => a.CreatedByInspectorId == requestingInspectorId.Value);
        }

        // Counted before paging, so TotalCount describes the whole filtered set.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Join(
                dbContext.Leads.AsNoTracking(),
                angebot => angebot.LeadId,
                lead => lead.Id,
                (angebot, lead) => new AngebotListItemDto(
                    angebot.Id,
                    angebot.AngebotNumber,
                    angebot.LeadId,
                    lead.Name,
                    angebot.Status,
                    angebot.NetTotal.Amount,
                    angebot.GrossTotal.Amount,
                    angebot.CreatedByInspectorId,
                    angebot.CreatedAt,
                    angebot.SentAt,
                    angebot.DecisionAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AngebotListItemDto>(items, page, pageSize, totalCount);
    }
}
