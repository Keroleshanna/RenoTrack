using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Project aggregate.</summary>
public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this Angebot has already been converted — ERD.md's <c>ANGEBOT ||--o| PROJECT</c>
    /// ("one Angebot converts to exactly one Project") asked as the business question rather than
    /// as a generic "get Project by AngebotId" the caller would then have to interpret
    /// (CLAUDE.md §4).
    ///
    /// <para>
    /// The unique index on <c>Projects.AngebotId</c> enforces the same rule, but D62's principle
    /// applies: a database constraint is a mechanism, not a business rule. Without this check an
    /// ordinary second click on "Convert to Project" would surface as an unmapped
    /// <c>DbUpdateException</c> → 500 instead of a 409. The index remains the concurrency backstop
    /// for the case this check cannot cover — two conversions racing between the check and the
    /// commit.
    /// </para>
    /// </summary>
    Task<bool> ExistsForAngebotAsync(int angebotId, CancellationToken cancellationToken);
}
