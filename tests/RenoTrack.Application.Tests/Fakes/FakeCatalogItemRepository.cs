using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>In-memory fake — no database, per Architecture.md §5.1's testable-in-isolation goal for the Application layer.</summary>
public sealed class FakeCatalogItemRepository : ICatalogItemRepository
{
    private readonly Dictionary<int, CatalogItem> _catalogItems = [];
    private int _nextId = 1;

    public List<CatalogItem> AddedCatalogItems { get; } = [];

    public Task AddAsync(CatalogItem catalogItem, CancellationToken cancellationToken)
    {
        AddedCatalogItems.Add(catalogItem);
        return Task.CompletedTask;
    }

    public Task<CatalogItem?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_catalogItems.GetValueOrDefault(id));

    /// <summary>
    /// Test-only seam simulating a CatalogItem that already exists in the database with a real,
    /// assigned Id — normally EF Core's job (Phase 3), not yet available. Uses reflection purely
    /// because CatalogItem.Id has no public setter by design; this stays inside test code and
    /// never touches production behavior.
    /// </summary>
    public CatalogItem Seed(CatalogItem catalogItem)
    {
        var id = _nextId++;
        typeof(CatalogItem).GetProperty(nameof(CatalogItem.Id))!.SetValue(catalogItem, id);
        _catalogItems[id] = catalogItem;
        return catalogItem;
    }
}
