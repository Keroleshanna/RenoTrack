using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;

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
}
