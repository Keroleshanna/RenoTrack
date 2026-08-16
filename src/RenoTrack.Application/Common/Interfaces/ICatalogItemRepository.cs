using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the CatalogItem aggregate (Architecture.md §5.1's read/write
/// split). Search/listing goes through ICatalogItemQueries instead — never through this
/// interface — since CatalogItem's read side returns DTOs directly without full aggregate
/// hydration.
/// </summary>
public interface ICatalogItemRepository
{
    /// <summary>Added for CreateCatalogItemCommand — the first use case for this repository.</summary>
    Task AddAsync(CatalogItem catalogItem, CancellationToken cancellationToken);

    /// <summary>Added for UpdateCatalogItemCommand — the first use case needing to load an existing CatalogItem.</summary>
    Task<CatalogItem?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// The Catalog entry already created from this Angebot line, or <c>null</c> if there is none.
    /// </summary>
    /// <remarks>
    /// Added so <c>SaveAngebotItemAsCatalogItemCommand</c> can be idempotent: FR-4.10 is a
    /// one-click action on a line, and a double-click, a retried request or a stale screen must not
    /// produce two library entries from one line. Named for the business question rather than as a
    /// generic filter (CLAUDE.md §4) — "which entry came from this line" is the only thing any
    /// caller wants to know about this column.
    /// </remarks>
    Task<CatalogItem?> GetByCreatedFromAngebotItemIdAsync(
        int angebotItemId,
        CancellationToken cancellationToken);
}
