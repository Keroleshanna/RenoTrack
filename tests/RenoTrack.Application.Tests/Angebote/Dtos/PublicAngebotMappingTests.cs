using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Angebote.Dtos;

/// <summary>
/// The customer-facing projection's ordering guarantees. What a customer reads is a priced
/// document, so the order of its parts is part of its content, not a detail of how it was stored.
/// </summary>
public class PublicAngebotMappingTests
{
    /// <summary>
    /// Simulates the database-assigned identity EF Core supplies, so ordering can be exercised at
    /// all: every freshly-created child shares <c>Id == 0</c> in memory. Reflection in test
    /// infrastructure only, exactly as the repository fakes' <c>Seed</c> helpers already do
    /// (<c>CLAUDE.md</c> §14) — production code needing this would be a bug, not a pattern.
    /// </summary>
    private static void AssignId<T>(T entity, int id) =>
        typeof(T).GetProperty("Id")!.SetValue(entity, id);

    /// <summary>
    /// <c>AngebotItem</c> has no <c>SortOrder</c> column and EF Core issues no <c>ORDER BY</c> for a
    /// collection navigation, so before this ordering existed the line order was whatever SQL Server
    /// returned. Ids are assigned in insertion order, so ordering by id reproduces the order the
    /// Inspector entered the lines — and makes the customer page's derived "Pos. 1.001" numbering
    /// mean something.
    /// </summary>
    /// <remarks>
    /// The ids are assigned deliberately out of insertion order, so a mapping that merely preserved
    /// the in-memory collection order would fail this test rather than pass it by accident.
    /// </remarks>
    [Fact]
    public void Items_are_ordered_by_id_not_by_the_collections_own_order()
    {
        var angebot = Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00042", createdByInspectorId: 5);
        var section = angebot.AddSection("Pos. 1 Abriss", 1);

        var first = angebot.AddItemToSection(section, "Wände abbrechen", 10m, ItemUnit.SquareMeter(), Money.FromExact(25.00m), VatRate.Standard);
        var second = angebot.AddItemToSection(section, "Schutt entsorgen", 2m, ItemUnit.LumpSum(), Money.FromExact(150.00m), VatRate.Standard);
        var third = angebot.AddItemToSection(section, "Gerüst stellen", 1m, ItemUnit.LumpSum(), Money.FromExact(400.00m), VatRate.Sixteen);

        AssignId(first, 30);
        AssignId(second, 10);
        AssignId(third, 20);

        var dto = angebot.ToPublicDto();

        Assert.Equal(
            ["Schutt entsorgen", "Gerüst stellen", "Wände abbrechen"],
            Assert.Single(dto.Sections).Items.Select(item => item.Description));
    }

    /// <summary>
    /// The section ordering this projection has always had, pinned alongside the new item ordering
    /// so the two cannot drift apart — <c>SortOrder</c> is never exposed to the customer, so it is
    /// the mapping alone that decides the order the company's document is read in.
    /// </summary>
    [Fact]
    public void Sections_are_ordered_by_sort_order_not_by_insertion()
    {
        var angebot = Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00042", createdByInspectorId: 5);

        var addedFirst = angebot.AddSection("Zweiter Abschnitt", sortOrder: 2);
        var addedSecond = angebot.AddSection("Erster Abschnitt", sortOrder: 1);
        AssignId(addedFirst, 1);
        AssignId(addedSecond, 2);

        var dto = angebot.ToPublicDto();

        Assert.Equal(
            ["Erster Abschnitt", "Zweiter Abschnitt"],
            dto.Sections.Select(section => section.Title));
    }

    /// <summary>
    /// A section with no items is preserved rather than dropped: the Inspector put it in the
    /// document, and only one section needs items for the Angebot to be submittable, so this is a
    /// reachable state rather than a hypothetical.
    /// </summary>
    [Fact]
    public void An_empty_section_survives_the_projection_with_a_zero_subtotal()
    {
        var angebot = Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00042", createdByInspectorId: 5);
        var priced = angebot.AddSection("Pos. 1 Abriss", 1);
        angebot.AddItemToSection(priced, "Wände abbrechen", 10m, ItemUnit.SquareMeter(), Money.FromExact(25.00m), VatRate.Standard);
        angebot.AddSection("Pos. 2 Noch offen", 2);

        var dto = angebot.ToPublicDto();

        Assert.Equal(2, dto.Sections.Count);
        var empty = dto.Sections[1];
        Assert.Equal("Pos. 2 Noch offen", empty.Title);
        Assert.Empty(empty.Items);
        Assert.Equal(0m, empty.Subtotal);
    }
}
