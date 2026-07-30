using RenoTrack.Application.CatalogItems;
using RenoTrack.Application.CatalogItems.Dtos;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// In-memory fake mirroring the BR-12 filtering a real implementation must perform (excludes
/// retired items) — not a dumb passthrough, so handler tests exercise the actual contract.
/// </summary>
public sealed class FakeCatalogItemQueries : ICatalogItemQueries
{
    private readonly List<CatalogItemDto> _items = [];

    public void Seed(CatalogItemDto item) => _items.Add(item);

    public Task<IReadOnlyList<CatalogItemDto>> SearchAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogItemDto>>(_items.Where(i => !i.IsRetired).ToList());
}
