using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// Lighter-weight than the full AngebotDto — returned alongside ItemDto from
/// AddAngebotItemCommand (Sequence Diagram §4) so a client adding items one at a time isn't
/// re-serializing the full header on every call.
/// </summary>
public sealed record AngebotSummaryDto(
    int Id,
    string AngebotNumber,
    AngebotStatus Status,
    decimal NetTotal,
    decimal GrossTotal);

public static class AngebotSummaryMappingExtensions
{
    public static AngebotSummaryDto ToSummaryDto(this Angebot angebot) => new(
        angebot.Id,
        angebot.AngebotNumber,
        angebot.Status,
        angebot.NetTotal.Amount,
        angebot.GrossTotal.Amount);
}
