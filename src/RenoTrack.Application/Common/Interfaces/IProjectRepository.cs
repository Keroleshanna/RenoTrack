using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Project aggregate.</summary>
public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a Project, or null if no such Project exists. Added in Phase 8 Slice 3, when
    /// <c>CreateInvoiceCommand</c> first needed the entity itself — to enforce StateMachine.md §5's
    /// "an Invoice cannot exist without an <c>Active</c>/<c>OnHold</c> Project", and to read the
    /// <c>AngebotId</c> whose VAT-rate mix the invoice amounts are derived from.
    ///
    /// <para>
    /// The write side, not <c>IProjectQueries</c>: the status guard is a business rule about the
    /// aggregate's own state, so the aggregate is what gets loaded (CLAUDE.md §6). Project owns no
    /// children, so this is a single-row read either way.
    /// </para>
    /// </summary>
    Task<Project?> GetByIdAsync(int id, CancellationToken cancellationToken);

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
