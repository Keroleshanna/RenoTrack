using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;

namespace RenoTrack.Application.Angebote.Queries.GetLeadAngebote;

public sealed class GetLeadAngeboteQueryHandler(IAngebotQueries angebotQueries)
    : IQueryHandler<GetLeadAngeboteQuery, IReadOnlyList<AngebotDto>>
{
    public Task<IReadOnlyList<AngebotDto>> HandleAsync(
        GetLeadAngeboteQuery query,
        CancellationToken cancellationToken) =>
        angebotQueries.GetForLeadAsync(query.LeadId, query.RequestingInspectorId, cancellationToken);
}
