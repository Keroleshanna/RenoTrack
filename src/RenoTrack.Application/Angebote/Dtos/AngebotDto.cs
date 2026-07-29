using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// Header-level view only — no nested Sections yet. Added incrementally: SectionDto, ItemDto,
/// and AngebotSummaryDto appear when AddAngebotSectionCommand/AddAngebotItemCommand actually
/// need to return them (Sequence Diagram §4), not speculatively now.
/// </summary>
public sealed record AngebotDto(
    int Id,
    int LeadId,
    int? InspectionId,
    string AngebotNumber,
    AngebotStatus Status,
    int CreatedByInspectorId,
    int? ReviewedByAdminId,
    DateTime? SentAt,
    DateTime? DecisionAt,
    DateTime CreatedAt,
    decimal NetTotal,
    decimal GrossTotal);

public static class AngebotMappingExtensions
{
    public static AngebotDto ToDto(this Angebot angebot) => new(
        angebot.Id,
        angebot.LeadId,
        angebot.InspectionId,
        angebot.AngebotNumber,
        angebot.Status,
        angebot.CreatedByInspectorId,
        angebot.ReviewedByAdminId,
        angebot.SentAt,
        angebot.DecisionAt,
        angebot.CreatedAt,
        angebot.NetTotal.Amount,
        angebot.GrossTotal.Amount);
}
