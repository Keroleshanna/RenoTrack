using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Application.Leads;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <inheritdoc />
/// <remarks>
/// Projects straight into <see cref="LeadDto"/> inside <c>Select(...)</c> rather than materializing
/// entities and calling <c>ToDto()</c>, which would force full-entity hydration — the same shape as
/// <c>CatalogItemQueries</c>, and the reason a read-side interface exists separately from the
/// repository at all. <c>AsNoTracking</c> throughout: nothing here is ever mutated, and tracking
/// every row of a pipeline page would be pure overhead.
/// </remarks>
public sealed class LeadQueries(RenoTrackDbContext dbContext) : ILeadQueries
{
    public async Task<PagedResult<LeadDto>> GetPagedAsync(
        LeadStatus? status,
        int? assignedInspectorId,
        DateTime? createdFrom,
        DateTime? createdTo,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Leads.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(lead => lead.Status == status.Value);
        }

        if (assignedInspectorId.HasValue)
        {
            query = query.Where(lead => lead.AssignedInspectorId == assignedInspectorId.Value);
        }

        if (createdFrom.HasValue)
        {
            query = query.Where(lead => lead.CreatedAt >= createdFrom.Value);
        }

        if (createdTo.HasValue)
        {
            query = query.Where(lead => lead.CreatedAt <= createdTo.Value);
        }

        // Counted before paging is applied, so TotalCount describes the whole filtered set rather
        // than the page — which is what a client needs to render page controls.
        var totalCount = await query.CountAsync(cancellationToken);

        // Newest first: Wireframe B2 is a work queue, where the most recent Lead is the one most
        // likely to need attention. Ordering is not optional — Skip/Take over an unordered query
        // has no defined result, so pages could silently repeat or omit rows. Id is the tiebreaker
        // because CreatedAt is not unique.
        var items = await query
            .OrderByDescending(lead => lead.CreatedAt)
            .ThenByDescending(lead => lead.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Select(lead => new LeadDto(
                lead.Id,
                lead.Name,
                lead.Phone,
                lead.Email,
                lead.Address,
                lead.Notes,
                lead.Source,
                lead.Status,
                lead.AssignedInspectorId,
                lead.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<LeadDto>(items, page, pageSize, totalCount);
    }
}
