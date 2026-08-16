using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A single line item within an AngebotSection. Child entity of the Angebot aggregate
/// (Architecture.md §6) — only ever created through its owning AngebotSection, never
/// directly, hence the <c>internal</c> constructor (same pattern as InspectionPhoto).
///
/// No FK back-reference to its owning AngebotSection is stored here (unlike
/// <see cref="CatalogItemId"/> below) — that relational link is purely structural and will be
/// handled as an EF Core shadow property in Phase 3, the same choice already made for
/// InspectionPhoto → Inspection.
///
/// <see cref="CatalogItemId"/> is the one exception: it is kept as real, visible data (not a
/// structural-only FK) because BR-8 gives it business meaning — traceability to the Catalog
/// entry this item may have been created from. No behavior on this type branches on whether it
/// is set; the copying of values from a CatalogItem is entirely the Application layer's
/// responsibility (BR-8, enforced by <c>AddAngebotItemCommand</c>).
///
/// No update methods are implemented yet. This is deliberately not documented as an
/// immutability rule — unlike BR-10 for Inspection, there is no explicit business evidence
/// that editing an existing item should be forbidden, only that no edit endpoint happens to be
/// documented yet. Until that is clarified, corrections go through removing and re-adding an
/// item via the owning AngebotSection.
/// </summary>
public sealed class AngebotItem
{
    public int Id { get; private set; }
    public int? CatalogItemId { get; private set; }
    public string Description { get; private set; }
    public string? Specification { get; private set; }
    public decimal Quantity { get; private set; }
    public ItemUnit Unit { get; private set; }
    public Money UnitPrice { get; private set; }
    public VatRate VatRate { get; private set; }

    internal AngebotItem(
        string description,
        decimal quantity,
        ItemUnit unit,
        Money unitPrice,
        VatRate vatRate,
        string? specification = null,
        int? catalogItemId = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitPrice.Amount < 0)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        Description = description.Trim();
        Specification = specification?.Trim();
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        VatRate = vatRate;
        CatalogItemId = catalogItemId;
    }

    /// <summary>
    /// Architecture.md §6.1 step 1, enforcing BR-11: rounds Quantity × UnitPrice through the
    /// single BR-11 policy rather than exposing the raw multiplication.
    /// </summary>
    /// <summary>
    /// Corrects this line's values. Reachable only through
    /// <c>Angebot.UpdateItem(section, item, ...)</c>, which owns the edit-lock
    /// (<c>EnsureEditable</c>) and recalculates the document's totals afterwards.
    ///
    /// <para>
    /// <b><c>internal</c>, like the constructor</b> — an aggregate root is the only public entry
    /// point for mutating its children (CLAUDE.md §2). A public setter or a public update here
    /// would let a caller change a price without the Angebot's totals ever being recomputed, which
    /// is precisely what <c>RecalculateTotals</c> being private exists to prevent.
    /// </para>
    /// <para>
    /// <b>Every guard the constructor applies is re-applied</b>, because these are lifetime
    /// invariants of a line — not creation-time conditions — so a correction must be held to the
    /// same standard as an insertion.
    /// </para>
    /// <para>
    /// <b><see cref="CatalogItemId"/> is deliberately untouched and is not a parameter.</b> It
    /// records where this line *came from* (BR-8), not that it still matches that entry — the copy
    /// is independent from the moment it is created, which is the whole point of copy-on-create.
    /// Editing a Catalog-sourced line is therefore legitimate, and clearing the link would destroy
    /// provenance to record a divergence BR-8 already anticipates. Nothing branches on the value.
    /// </para>
    /// </summary>
    internal void Update(
        string description,
        decimal quantity,
        ItemUnit unit,
        Money unitPrice,
        VatRate vatRate,
        string? specification = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitPrice.Amount < 0)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        Description = description.Trim();
        Specification = specification?.Trim();
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        VatRate = vatRate;
    }

    public Money LineTotal => Money.RoundedPerBR11(Quantity * UnitPrice.Amount);
}
