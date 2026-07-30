using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Commands.AddAngebotItem;

/// <summary>
/// SRS FR-4.9/BR-8: covers both the Catalog-sourced path (CatalogItemId set — Description/
/// Specification/UnitCode are ignored, since the handler copies Title/DefaultSpecification/
/// DefaultUnit from the CatalogItem) and the custom path (CatalogItemId null — those three
/// fields are required directly from the caller). Quantity, UnitPrice, and VatRate are always
/// caller-supplied for both paths (Sequence Diagram §4 line 210: the Catalog-path wire request
/// is `{ catalogItemId, qty, unitPrice, vatRate }` — UnitPrice is a confirmed/adjusted value,
/// never silently taken from CatalogItem.SuggestedUnitPrice).
/// </summary>
public sealed record AddAngebotItemCommand(
    int AngebotId,
    int SectionId,
    int? CatalogItemId,
    string? Description,
    string? Specification,
    string? UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    VatRate VatRate,
    int InspectorId);
