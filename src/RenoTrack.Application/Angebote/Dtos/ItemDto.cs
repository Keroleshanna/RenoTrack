using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>No SectionId — AngebotItem itself has no such property (the FK is an EF shadow property, Phase 3); the client already knows the section it posted to.</summary>
public sealed record ItemDto(
    int Id,
    int? CatalogItemId,
    string Description,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    VatRate VatRate,
    decimal LineTotal);

public static class ItemMappingExtensions
{
    public static ItemDto ToDto(this AngebotItem item) => new(
        item.Id,
        item.CatalogItemId,
        item.Description,
        item.Specification,
        item.Quantity,
        item.Unit.Code,
        item.UnitPrice.Amount,
        item.VatRate,
        item.LineTotal.Amount);
}
