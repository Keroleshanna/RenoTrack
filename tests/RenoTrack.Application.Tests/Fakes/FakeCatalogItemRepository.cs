using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>In-memory fake — no database, per Architecture.md §5.1's testable-in-isolation goal for the Application layer.</summary>
public sealed class FakeCatalogItemRepository : ICatalogItemRepository
{
    public List<CatalogItem> AddedCatalogItems { get; } = [];

    public Task AddAsync(CatalogItem catalogItem, CancellationToken cancellationToken)
    {
        AddedCatalogItems.Add(catalogItem);
        return Task.CompletedTask;
    }
}
