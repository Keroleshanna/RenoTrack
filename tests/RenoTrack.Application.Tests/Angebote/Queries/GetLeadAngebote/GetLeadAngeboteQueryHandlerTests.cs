using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetLeadAngebote;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Angebote.Queries.GetLeadAngebote;

/// <summary>
/// The handler is a pass-through, so what is worth asserting is that the scope reaches the query
/// unchanged — an Inspector as their own id, an Admin as null. Getting that wrong is what would
/// silently widen or narrow visibility, and it is not something the query implementation can fix.
/// </summary>
public class GetLeadAngeboteQueryHandlerTests
{
    private readonly FakeAngebotQueries _angebotQueries = new();
    private readonly GetLeadAngeboteQueryHandler _handler;

    public GetLeadAngeboteQueryHandlerTests()
    {
        _handler = new GetLeadAngeboteQueryHandler(_angebotQueries);
    }

    [Fact]
    public async Task HandleAsync_PassesTheInspectorScopeThrough()
    {
        await _handler.HandleAsync(new GetLeadAngeboteQuery(LeadId: 7, RequestingInspectorId: 5), CancellationToken.None);

        var call = Assert.Single(_angebotQueries.Calls);
        Assert.Equal(7, call.LeadId);
        Assert.Equal(5, call.RequestingInspectorId);
    }

    [Fact]
    public async Task HandleAsync_PassesNullForAnAdmin()
    {
        await _handler.HandleAsync(new GetLeadAngeboteQuery(LeadId: 7, RequestingInspectorId: null), CancellationToken.None);

        var call = Assert.Single(_angebotQueries.Calls);
        Assert.Null(call.RequestingInspectorId);
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhatTheQueryReturns()
    {
        _angebotQueries.Result =
        [
            new AngebotDto(1, 7, null, "ANG-2026-00002", AngebotStatus.Draft, 5, null, null, null, DateTime.UtcNow, 10m, 11.90m),
            new AngebotDto(2, 7, null, "ANG-2026-00001", AngebotStatus.CustomerRejected, 5, 3, null, null, DateTime.UtcNow, 20m, 23.80m),
        ];

        var result = await _handler.HandleAsync(
            new GetLeadAngeboteQuery(LeadId: 7, RequestingInspectorId: 5), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("ANG-2026-00002", result[0].AngebotNumber);
    }
}
