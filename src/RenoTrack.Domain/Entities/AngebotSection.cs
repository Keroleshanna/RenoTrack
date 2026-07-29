using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A section within an Angebot (e.g. "Pos. 1 Baustelleneinrichtung"), owning its own
/// AngebotItem children. Child entity of the Angebot aggregate (Architecture.md §6) — only
/// ever created through <c>Angebot.AddSection</c>, hence the <c>internal</c> constructor.
///
/// <see cref="AddItem"/> is also <c>internal</c>, not public: the only supported public entry
/// point for adding an item is <c>Angebot.AddItemToSection(...)</c>, which calls this method
/// and then recalculates the Angebot's cached NetTotal/GrossTotal in the same call — see
/// Architecture.md §6.2 for why (the alternative would let a caller add an item without ever
/// triggering that recalculation).
///
/// An AngebotSection with zero items is a normal, valid state during editing — SRS's own
/// Sequence Diagram §4 shows a section being created before any items exist. The "at least one
/// section with at least one item" guard (StateMachine.md §2.3) applies to the whole Angebot at
/// submission time, not to each individual section, so it is not enforced here.
/// </summary>
public sealed class AngebotSection
{
    private readonly List<AngebotItem> _items = [];

    public int Id { get; private set; }
    public string Title { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyList<AngebotItem> Items => _items;

    /// <summary>Computed, never stored — Σ(items.LineTotal). See Architecture.md §6.1 for why this is never cached the way Angebot.NetTotal/GrossTotal are.</summary>
    public Money Subtotal => Money.Sum(_items.Select(i => i.LineTotal));

    internal AngebotSection(string title, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Section title is required.", nameof(title));

        Title = title.Trim();
        SortOrder = sortOrder;
    }

    internal AngebotItem AddItem(
        string description,
        decimal quantity,
        ItemUnit unit,
        Money unitPrice,
        VatRate vatRate,
        string? specification = null,
        int? catalogItemId = null)
    {
        var item = new AngebotItem(description, quantity, unit, unitPrice, vatRate, specification, catalogItemId);
        _items.Add(item);
        return item;
    }
}
