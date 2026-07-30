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
}
