using RenoTrack.Application.CatalogItems.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.CatalogItems;

public interface ICatalogItemQueries
{
    /// <summary>
    /// Non-retired Catalog items, optionally narrowed by a search term, ordered by title.
    /// </summary>
    /// <remarks>
    /// Retired items are excluded unconditionally and there is no flag to include them (BR-12, D37) —
    /// no document shows retired items surfaced anywhere.
    /// </remarks>
    Task<PagedResult<CatalogItemDto>> SearchAsync(
        string? searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Of the given Angebot line ids, those a Catalog entry was already created from (FR-4.10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the reverse direction of <c>AngebotItem.CatalogItemId</c>, and confusing the two
    /// was a real defect.</b> <c>CatalogItemId</c> records that a line was created *from* the
    /// Catalog (BR-8); this answers whether a line has been contributed *to* it. A line can have
    /// either, both or neither, so neither field can stand in for the other — the screen that used
    /// <c>CatalogItemId</c> to decide whether to offer "save as Catalog item" therefore offered it
    /// forever, including on lines that had already been saved.
    /// </para>
    /// <para>
    /// A query rather than a Domain navigation, because <c>CatalogItem</c> and <c>Angebot</c> are
    /// independent aggregates related by id only (CLAUDE.md §2). Batched over the whole document
    /// rather than asked per line, so rendering a quote stays one round trip.
    /// </para>
    /// </remarks>
    Task<IReadOnlySet<int>> GetAngebotItemIdsWithCatalogEntryAsync(
        IReadOnlyCollection<int> angebotItemIds,
        CancellationToken cancellationToken);
}
