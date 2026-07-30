using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.CatalogItems.Dtos;

public sealed record CatalogItemDto(
    int Id,
    string Title,
    string? DefaultSpecification,
    string DefaultUnit,
    decimal SuggestedUnitPrice,
    int? CreatedFromAngebotItemId,
    bool IsRetired,
    DateTime CreatedAt);

public static class CatalogItemMappingExtensions
{
    public static CatalogItemDto ToDto(this CatalogItem catalogItem) => new(
        catalogItem.Id,
        catalogItem.Title,
        catalogItem.DefaultSpecification,
        catalogItem.DefaultUnit.Code,
        catalogItem.SuggestedUnitPrice.Amount,
        catalogItem.CreatedFromAngebotItemId,
        catalogItem.IsRetired,
        catalogItem.CreatedAt);
}
