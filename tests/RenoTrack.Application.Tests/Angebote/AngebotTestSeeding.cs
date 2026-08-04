using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Angebote;

/// <summary>
/// Assigns the database-generated ids EF Core would assign to an Angebot's children, so a handler
/// that resolves a section or item <em>by id</em> can be tested at all.
/// </summary>
/// <remarks>
/// Reflection, and test-only — the same explicitly sanctioned seam as every fake repository's
/// <c>Seed</c> method (CLAUDE.md §14). Child ids are <c>private set</c> and are normally assigned by
/// persistence; without this every freshly created child shares <c>Id == 0</c>, which is exactly the
/// ambiguity Architecture.md §6 describes.
/// </remarks>
internal static class AngebotTestSeeding
{
    public static void AssignChildIds(this Angebot angebot)
    {
        var nextId = 1;

        foreach (var section in angebot.Sections)
        {
            SetId(section, nextId++);

            foreach (var item in section.Items)
            {
                SetId(item, nextId++);
            }
        }
    }

    private static void SetId(object entity, int id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);
}
