using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Dtos;

public sealed record InspectionDto(
    int Id,
    int LeadId,
    DateTime ScheduledAt,
    int InspectorId,
    string? Notes,
    DateTime? CompletedAt);

public static class InspectionMappingExtensions
{
    public static InspectionDto ToDto(this Inspection inspection) => new(
        inspection.Id,
        inspection.LeadId,
        inspection.ScheduledAt,
        inspection.InspectorId,
        inspection.Notes,
        inspection.CompletedAt);
}
