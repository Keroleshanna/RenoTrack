using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// Project owns no children and holds no navigation properties, so no Include is needed.
///
/// <para>
/// <c>ExistsForAngebotAsync</c> is a plain <c>AnyAsync</c> existence check — the same shape as
/// <c>AngebotRepository.HasActiveAngebotForLeadAsync</c>, and for the same reason: the caller
/// needs the answer, not the entity. It is the Application-level pre-check that turns an ordinary
/// second conversion attempt into a 409; the unique index on <c>Projects.AngebotId</c> remains the
/// backstop for two attempts racing past it (D62).
/// </para>
/// </summary>
public sealed class ProjectRepository(RenoTrackDbContext dbContext) : IProjectRepository
{
    public async Task AddAsync(Project project, CancellationToken cancellationToken) =>
        await dbContext.Projects.AddAsync(project, cancellationToken);

    public async Task<bool> ExistsForAngebotAsync(int angebotId, CancellationToken cancellationToken) =>
        await dbContext.Projects.AnyAsync(p => p.AngebotId == angebotId, cancellationToken);

    /// <summary>
    /// <c>FindAsync</c>, not <c>FirstOrDefaultAsync</c> — Project has no navigation properties, so
    /// there is nothing to <c>Include</c> and no reason to bypass the identity-map lookup. The
    /// tracked result is what a future command that loads-then-mutates a Project will rely on,
    /// since no <c>UpdateAsync</c> exists anywhere in this project.
    /// </summary>
    public async Task<Project?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Projects.FindAsync([id], cancellationToken);
}
