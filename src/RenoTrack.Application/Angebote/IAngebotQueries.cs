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

    /// <summary>
    /// One line item, found by its own id. Needed by FR-4.10 "save as Catalog item", whose route
    /// (<c>POST /api/v1/angebot-items/{id}/save-as-catalog-item</c>) names the item alone.
    /// </summary>
    /// <remarks>
    /// A read rather than a repository load: the caller copies values out of the item and never
    /// mutates it or its Angebot, so hydrating the whole aggregate to reach one child would be work
    /// with no purpose. Access needs no scope parameter — PermissionMatrix.md §3 marks this action
    /// "F" for Inspectors, since the Catalog is shared company-wide rather than per-Lead.
    /// </remarks>
    Task<ItemDto?> GetItemAsync(int itemId, CancellationToken cancellationToken);
}
