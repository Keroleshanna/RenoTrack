using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>No nested Items yet — added when a use case needs to return them.</summary>
public sealed record SectionDto(int Id, string Title, int SortOrder, decimal Subtotal);

public static class SectionMappingExtensions
{
    public static SectionDto ToDto(this AngebotSection section) =>
        new(section.Id, section.Title, section.SortOrder, section.Subtotal.Amount);
}
