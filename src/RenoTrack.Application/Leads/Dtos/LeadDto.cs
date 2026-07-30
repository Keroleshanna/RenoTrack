using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Leads.Dtos;

public sealed record LeadDto(
    int Id,
    string Name,
    string Phone,
    string Email,
    string? Address,
    string? Notes,
    LeadSource Source,
    LeadStatus Status,
    int? AssignedInspectorId,
    DateTime CreatedAt);

public static class LeadMappingExtensions
{
    public static LeadDto ToDto(this Lead lead) => new(
        lead.Id,
        lead.Name,
        lead.Phone,
        lead.Email,
        lead.Address,
        lead.Notes,
        lead.Source,
        lead.Status,
        lead.AssignedInspectorId,
        lead.CreatedAt);
}
