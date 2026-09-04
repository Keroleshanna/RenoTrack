using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Inspections;
using RenoTrack.Application.Inspections.Dtos;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <inheritdoc />
/// <remarks>
/// The Lead's contact details are joined inside the projection — the aggregates still relate by id
/// only, which is exactly the latitude a read-side interface provides (D36). <c>Photos</c> is counted
/// rather than materialised: the count is what a schedule needs, and the storage keys are not
/// servable yet in any case.
/// </remarks>
public sealed class InspectionQueries(RenoTrackDbContext dbContext) : IInspectionQueries
{
    public async Task<InspectionDetailDto?> GetByIdAsync(
        int id,
        int? requestingInspectorId,
        CancellationToken cancellationToken) =>
        await Project(dbContext.Inspections.AsNoTracking().Where(i => i.Id == id), requestingInspectorId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<InspectionDetailDto>> GetScheduledAsync(
        DateTime from,
        DateTime to,
        int? requestingInspectorId,
        bool includeCompleted,
        CancellationToken cancellationToken)
    {
        // `to` is exclusive so a caller can ask for exactly one day without straddling midnight.
        var query = dbContext.Inspections
            .AsNoTracking()
            .Where(i => i.ScheduledAt >= from && i.ScheduledAt < to);

        if (!includeCompleted)
        {
            query = query.Where(i => i.CompletedAt == null);
        }

        // Ordered on the ENTITY, before the projection — ordering the projected DTO instead puts the
        // sort after a join that already carries a correlated `Photos.Count`, and EF cannot translate
        // that shape. Earliest first, because a schedule is read in the order the day happens; Id
        // breaks ties, since two visits can share an appointment time.
        var ordered = query.OrderBy(i => i.ScheduledAt).ThenBy(i => i.Id);

        return await Project(ordered, requestingInspectorId).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The shared projection, so the single read and the schedule can never disagree about what an
    /// Inspection looks like.
    /// </summary>
    private IQueryable<InspectionDetailDto> Project(
        IQueryable<Domain.Entities.Inspection> source,
        int? requestingInspectorId)
    {
        // Null means Admin — "F" in PermissionMatrix.md §2, so no restriction. An Inspector is "S"
        // and sees only their own assignments, enforced in SQL rather than after loading.
        if (requestingInspectorId.HasValue)
        {
            source = source.Where(i => i.InspectorId == requestingInspectorId.Value);
        }

        return source.Join(
            dbContext.Leads.AsNoTracking(),
            inspection => inspection.LeadId,
            lead => lead.Id,
            (inspection, lead) => new InspectionDetailDto(
                inspection.Id,
                inspection.LeadId,
                lead.Name,
                lead.Address,
                lead.Phone,
                inspection.ScheduledAt,
                inspection.InspectorId,
                inspection.Notes,
                inspection.CompletedAt,

                // Counted in SQL, never materialised — see the class note.
                inspection.Photos.Count));
    }
}
