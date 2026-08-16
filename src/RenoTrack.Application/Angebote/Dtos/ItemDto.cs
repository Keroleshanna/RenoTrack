using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>No SectionId — AngebotItem itself has no such property (the FK is an EF shadow property, Phase 3); the client already knows the section it posted to.</summary>
/// <param name="CatalogItemId">
/// The Catalog entry this line was created <i>from</i> (BR-8), or <c>null</c> for a hand-written
/// line. Traceability only — nothing branches on it.
/// </param>
/// <param name="SavedToCatalog">
/// Whether a Catalog entry has already been created <i>from</i> this line (FR-4.10).
///
/// <para>
/// <b>The opposite direction to <see cref="CatalogItemId"/>, and the two are not interchangeable.</b>
/// A line can have neither (hand-written, not contributed), either, or both. This field exists
/// because the screen previously used <see cref="CatalogItemId"/> to decide whether to offer "save
/// as Catalog item" — which is a different question, so the action stayed on offer after a line had
/// already been contributed, and a second click created a second library entry.
/// </para>
/// <para>
/// A boolean rather than the Catalog entry's id: the caller's only question is whether the action
/// is still available, and exposing the id would invite a screen to link a quote line to a library
/// entry that BR-8 makes deliberately independent of it.
/// </para>
/// </param>
public sealed record ItemDto(
    int Id,
    int? CatalogItemId,
    string Description,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    VatRate VatRate,
    decimal LineTotal,
    bool SavedToCatalog);

public static class ItemMappingExtensions
{
    /// <param name="savedToCatalog">
    /// Supplied by the caller, because the aggregate cannot know it: <c>CatalogItem</c> is an
    /// independent aggregate related by id only (CLAUDE.md §2), so the fact lives in a query, not
    /// in this object graph. Defaults to <see langword="false"/> for the write-side responses,
    /// where the question is not being asked and no extra round trip is warranted.
    /// </param>
    public static ItemDto ToDto(this AngebotItem item, bool savedToCatalog = false) => new(
        item.Id,
        item.CatalogItemId,
        item.Description,
        item.Specification,
        item.Quantity,
        item.Unit.Code,
        item.UnitPrice.Amount,
        item.VatRate,
        item.LineTotal.Amount,
        savedToCatalog);
}
