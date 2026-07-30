using RenoTrack.Application.CatalogItems.Dtos;

namespace RenoTrack.Application.CatalogItems;

/// <summary>
/// Read-side query interface for CatalogItem — returns DTOs directly, never a repository, never
/// a hydrated CatalogItem aggregate (Architecture.md §5.1's CQRS-lite read/write split; see
/// ARCHITECTURE_DECISIONS.md D36). Deliberately lives in the CatalogItems feature folder, not
/// Common.Interfaces, since its return type is a feature DTO — Common must not depend on any
/// feature folder (the same reasoning that moved NewWebsiteLeadNotification out of
/// Common.Notifications' original LeadDto dependency, see D23).
/// </summary>
public interface ICatalogItemQueries
{
    /// <summary>
    /// BR-12: always excludes retired items (Wireframes.md D2/F1 show no "include retired"
    /// affordance anywhere). No search term, sorting, or pagination parameter — none is
    /// documented as a required part of this query's contract (see ARCHITECTURE_DECISIONS.md
    /// D37).
    /// </summary>
    Task<IReadOnlyList<CatalogItemDto>> SearchAsync(CancellationToken cancellationToken);
}
