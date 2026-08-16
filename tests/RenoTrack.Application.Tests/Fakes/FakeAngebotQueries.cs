using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Records the arguments it was called with, because the whole point of the Lead-Angebote read is
/// <em>which scope reached the query</em> — an Admin must arrive as null and an Inspector as their
/// own id. Returning canned rows is secondary.
/// </summary>
public sealed class FakeAngebotQueries : IAngebotQueries
{
    public List<(int LeadId, int? RequestingInspectorId)> Calls { get; } = [];
    public IReadOnlyList<AngebotDto> Result { get; set; } = [];

    public Task<IReadOnlyList<AngebotDto>> GetForLeadAsync(
        int leadId,
        int? requestingInspectorId,
        CancellationToken cancellationToken)
    {
        Calls.Add((leadId, requestingInspectorId));
        return Task.FromResult(Result);
    }

    /// <summary>Items seeded by id, for the FR-4.10 "save as Catalog item" handler.</summary>
    public Dictionary<int, ItemDto> Items { get; } = [];

    public Task<ItemDto?> GetItemAsync(int itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.GetValueOrDefault(itemId));

    /// <summary>
    /// Records the arguments the cross-Lead list was called with, for the same reason
    /// <see cref="GetForLeadAsync"/> does: what matters is that an Admin arrives as null and an
    /// Inspector as their own id, so a scoping regression fails a test rather than leaking data.
    /// </summary>
    public List<(AngebotStatus? Status, int? RequestingInspectorId, int Page, int PageSize)> PagedCalls { get; } = [];

    public PagedResult<AngebotListItemDto> PagedResult { get; set; } = new([], 1, 25, 0);

    public Task<PagedResult<AngebotListItemDto>> GetPagedAsync(
        AngebotStatus? status,
        int? requestingInspectorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        PagedCalls.Add((status, requestingInspectorId, page, pageSize));
        return Task.FromResult(PagedResult);
    }
}
