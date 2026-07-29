using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A reusable line-item template (SRS FR-4.8). Independent aggregate root (Architecture.md
/// §6) — owns nothing, and is never live-referenced by AngebotItem: BR-8 requires every
/// AngebotItem to copy these values at creation time rather than joining to this entity, so
/// editing or retiring a CatalogItem can never retroactively change a past Angebot.
///
/// <see cref="CreatedFromAngebotItemId"/> is set once at creation and never changes — it
/// records provenance (grown organically via SRS FR-4.10's "save as Catalog item" action) but
/// carries no behavioral significance: nothing on this type branches on whether it is set,
/// mirroring how <see cref="AngebotItem.CatalogItemId"/> is passive traceability data on the
/// other side of this same relationship. Whether a given <see cref="Create"/> call originates
/// from Admin curation (SRS FR-4.8) or an Inspector's "save as" action (FR-4.10) is an
/// authorization concern for the Application layer, not a Domain distinction — both paths
/// produce the same shape, so there is a single factory rather than two.
/// </summary>
public sealed class CatalogItem
{
    public int Id { get; private set; }
    public string Title { get; private set; }
    public string? DefaultSpecification { get; private set; }
    public ItemUnit DefaultUnit { get; private set; }
    public Money SuggestedUnitPrice { get; private set; }
    public int? CreatedFromAngebotItemId { get; private set; }
    public bool IsRetired { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CatalogItem(
        string title,
        ItemUnit defaultUnit,
        Money suggestedUnitPrice,
        string? defaultSpecification,
        int? createdFromAngebotItemId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (suggestedUnitPrice.Amount < 0)
            throw new ArgumentException("Suggested unit price cannot be negative.", nameof(suggestedUnitPrice));

        Title = title.Trim();
        DefaultSpecification = defaultSpecification?.Trim();
        DefaultUnit = defaultUnit;
        SuggestedUnitPrice = suggestedUnitPrice;
        CreatedFromAngebotItemId = createdFromAngebotItemId;
        IsRetired = false;
        CreatedAt = DateTime.UtcNow;
    }

    public static CatalogItem Create(
        string title,
        ItemUnit defaultUnit,
        Money suggestedUnitPrice,
        string? defaultSpecification = null,
        int? createdFromAngebotItemId = null) =>
        new(title, defaultUnit, suggestedUnitPrice, defaultSpecification, createdFromAngebotItemId);

    /// <summary>
    /// PermissionMatrix.md §6: Admin-only editing (enforced at the Application/API layer, not
    /// here). Never changes <see cref="CreatedFromAngebotItemId"/> or <see cref="CreatedAt"/> —
    /// both are historical facts about this item's origin, not editable properties. BR-8: any
    /// AngebotItem already created from this CatalogItem already holds its own copy of the old
    /// values and is entirely unaffected by this call.
    /// </summary>
    public void Update(string title, ItemUnit defaultUnit, Money suggestedUnitPrice, string? defaultSpecification = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (suggestedUnitPrice.Amount < 0)
            throw new ArgumentException("Suggested unit price cannot be negative.", nameof(suggestedUnitPrice));

        Title = title.Trim();
        DefaultSpecification = defaultSpecification?.Trim();
        DefaultUnit = defaultUnit;
        SuggestedUnitPrice = suggestedUnitPrice;
    }

    /// <summary>
    /// BR-12: retiring, never deleting. Excludes this item from the Catalog picker
    /// (Wireframes.md D2) going forward while preserving it — and every AngebotItem's
    /// traceability link to it (BR-8) — indefinitely. Idempotent: retiring an
    /// already-retired item is a no-op, not an error, since there is no meaningful
    /// "from" state to guard here (unlike an AngebotStatus transition).
    /// </summary>
    public void Retire() => IsRetired = true;
}
