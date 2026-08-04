using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Application.Angebote;

/// <summary>
/// Read-side access to Angebote, bypassing aggregate hydration (D36). Grows on demand, exactly like
/// every repository interface in this project (CLAUDE.md §4).
/// </summary>
public interface IAngebotQueries
{
    /// <summary>
    /// Every Angebot belonging to one Lead, newest first.
    /// </summary>
    /// <param name="requestingInspectorId">
    /// The Inspector whose scope applies, or <see langword="null"/> for an Admin ("F" in
    /// PermissionMatrix.md §3–4). A collection read is scoped by a <c>WHERE</c> clause rather than
    /// by <c>IOwnershipValidator</c>, because ownership-checking a collection after loading would
    /// mean loading everything first (CLAUDE.md §22).
    /// </param>
    Task<IReadOnlyList<AngebotDto>> GetForLeadAsync(
        int leadId,
        int? requestingInspectorId,
        CancellationToken cancellationToken);
}
