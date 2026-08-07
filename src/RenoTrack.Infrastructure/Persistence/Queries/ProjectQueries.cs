using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Projects;
using RenoTrack.Application.Projects.Dtos;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// The Project detail read, projected in SQL across three tables.
///
/// <para>
/// <b>Joined explicitly rather than navigated.</b> `Project` holds no navigation property to
/// `Customer` or `Angebot` by design (CLAUDE.md §2), so there is nothing to <c>Include</c> — the
/// joins are written out. That is the read side paying a small, visible cost for a write-side
/// guarantee, not a workaround.
/// </para>
/// <para>
/// <b><c>LeadId</c> comes from the Angebot</b>, the originating document E1's "Originating:" line
/// names. `Customer.LeadId` holds the same value by construction — the conversion handler resolves
/// the Customer by the Angebot's own Lead — so the choice is about which one *means* "the Lead this
/// work came from", not about which happens to be populated.
/// </para>
/// </summary>
public sealed class ProjectQueries(RenoTrackDbContext dbContext) : IProjectQueries
{
    public async Task<ProjectDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await (from project in dbContext.Projects.AsNoTracking()
               join customer in dbContext.Customers.AsNoTracking() on project.CustomerId equals customer.Id
               join angebot in dbContext.Angebote.AsNoTracking() on project.AngebotId equals angebot.Id
               where project.Id == id
               select new ProjectDetailDto(
                   project.Id,
                   project.Status,
                   project.AgreedTotal.Amount,
                   project.CreatedAt,
                   project.CompletedAt,
                   customer.Id,
                   customer.Name,
                   angebot.LeadId,
                   angebot.InspectionId,
                   angebot.Id,
                   angebot.AngebotNumber))
            .SingleOrDefaultAsync(cancellationToken);
}
