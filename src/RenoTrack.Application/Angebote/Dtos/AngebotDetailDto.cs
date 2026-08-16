using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// One VAT-rate line of the Angebot's breakdown — Architecture.md §6.1 step 4, matching the sample
/// document's "zzgl. 0% MwSt / zzgl. 16% MwSt / zzgl. 19% MwSt" rows. Always computed from the live
/// item collection; there is no ERD column for it and nothing caches it.
/// </summary>
public sealed record VatBreakdownLineDto(VatRate Rate, decimal NetAmount, decimal VatAmount);

/// <summary>
/// A section together with its items, for the one use case that returns the whole tree.
/// </summary>
/// <remarks>
/// Deliberately a second type rather than adding <c>Items</c> to <see cref="SectionDto"/>:
/// <c>SectionDto</c> is what <c>AddAngebotSectionCommand</c> returns, where a freshly created
/// section provably has no items, so growing it would add an always-empty list to that response.
/// Same growth-on-demand discipline CLAUDE.md §7 applies to DTOs generally.
/// </remarks>
public sealed record SectionDetailDto(
    int Id,
    string Title,
    int SortOrder,
    decimal Subtotal,
    IReadOnlyList<ItemDto> Items);

/// <summary>
/// The full Angebot: header, the section/item tree, and the VAT breakdown. This is what the builder
/// screen and the Admin review screen both read (Wireframes D1–D3).
/// </summary>
/// <remarks>
/// Introduced in Phase 5 because this is the first use case that actually returns the tree — the
/// header-only <see cref="AngebotDto"/> remains correct for the create/transition responses that
/// have no reason to serialise it.
/// </remarks>
public sealed record AngebotDetailDto(
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
    decimal GrossTotal,
    IReadOnlyList<VatBreakdownLineDto> VatBreakdown,
    IReadOnlyList<SectionDetailDto> Sections);

public static class AngebotDetailMappingExtensions
{
    /// <param name="itemIdsSavedToCatalog">
    /// Line ids a Catalog entry has already been created from (FR-4.10), supplied by the caller
    /// from <c>ICatalogItemQueries</c>. The aggregate cannot know this — <c>CatalogItem</c> is an
    /// independent aggregate related by id only — so it is threaded in rather than looked up here.
    /// An empty set is the honest default: it means "not asked", and the only screen that asks is
    /// the one offering the action.
    /// </param>
    public static AngebotDetailDto ToDetailDto(
        this Angebot angebot,
        IReadOnlySet<int>? itemIdsSavedToCatalog = null) => new(
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
        angebot.GrossTotal.Amount,
        [.. angebot.VatBreakdown.Select(line =>
            new VatBreakdownLineDto(line.Rate, line.NetAmount.Amount, line.VatAmount.Amount))],

        // Ordered by SortOrder so the response reflects the document's own layout rather than
        // whatever order EF Core happened to materialise the collection in.
        [.. angebot.Sections
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .Select(section => section.ToDetailDto(itemIdsSavedToCatalog))]);

    public static SectionDetailDto ToDetailDto(
        this AngebotSection section,
        IReadOnlySet<int>? itemIdsSavedToCatalog = null) => new(
        section.Id,
        section.Title,
        section.SortOrder,
        section.Subtotal.Amount,
        [.. section.Items.Select(item =>
            item.ToDto(itemIdsSavedToCatalog?.Contains(item.Id) ?? false))]);
}
