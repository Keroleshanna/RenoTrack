using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotItem;

/// <summary>Matches Sequence Diagram §4's "ItemDto + updated AngebotSummaryDto" response — a named composite, not a raw tuple.</summary>
public sealed record AddAngebotItemResult(ItemDto Item, AngebotSummaryDto Summary);
