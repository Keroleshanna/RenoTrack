using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The <c>CatalogItems.CreatedFromAngebotItemId → AngebotItems</c> relationship, against the real
/// foreign key.
/// </summary>
/// <remarks>
/// <para>
/// <b>These exist because the constraint was <c>Restrict</c> and that was wrong.</b> Removing a
/// draft line that had been contributed to the Catalog (FR-4.10) failed with a
/// <c>DbUpdateException</c> on this FK, surfacing as an unmapped 500 — found by removing a line in
/// the running Dashboard, not by review. It contradicted CLAUDE.md §2, which states plainly that a
/// line in a <c>Draft</c>/<c>ChangesRequested</c> Angebot is unsent working material and may be
/// removed.
/// </para>
/// <para>
/// The relationship is now <c>SetNull</c>: the line goes, the Catalog entry survives (BR-12), and
/// its provenance link becomes <c>NULL</c> — the honest record of an entry whose origin no longer
/// exists. Nothing branches on that link (BR-8 makes the copy independent at creation), so nulling
/// it costs no behaviour.
/// </para>
/// <para>
/// LocalDB, not InMemory: a referential action is exactly the kind of real SQL constraint the
/// InMemory provider does not enforce, so only a real database can prove this (CLAUDE.md §14).
/// </para>
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class CatalogItemOriginLinkTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task Removing_an_ordinary_line_leaves_the_angebot_valid()
    {
        var angebotId = await SeedAngebotWithOneItemAsync();

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);
            var section = angebot.Sections.Single();
            angebot.RemoveItem(section, section.Items.Single());
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await LoadAsync(readContext, angebotId);

        Assert.Empty(reloaded.Sections.Single().Items);
        Assert.Equal(0m, reloaded.NetTotal.Amount);
    }

    [Fact]
    public async Task Removing_a_line_that_was_saved_to_the_catalog_succeeds()
    {
        var angebotId = await SeedAngebotWithOneItemAsync();
        int itemId;

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);
            itemId = angebot.Sections.Single().Items.Single().Id;

            // FR-4.10 — the line is contributed to the shared library.
            context.CatalogItems.Add(CatalogItem.Create(
                "Fliesen verlegen",
                ItemUnit.SquareMeter(),
                Money.FromExact(82.25m),
                createdFromAngebotItemId: itemId));

            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);
            var section = angebot.Sections.Single();

            // Under Restrict this threw DbUpdateException and reached the client as a 500.
            angebot.RemoveItem(section, section.Items.Single());
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();

        Assert.Empty((await LoadAsync(readContext, angebotId)).Sections.Single().Items);

        // Set<T>() rather than a DbSet: AngebotItem deliberately has none, because a child entity is
        // only reachable through its aggregate root (CLAUDE.md §21). The row is still checked
        // directly here, since "the line is gone" is the fact under test.
        Assert.False(await readContext.Set<AngebotItem>().AnyAsync(i => i.Id == itemId));
    }

    [Fact]
    public async Task The_catalog_entry_survives_the_removal_with_a_null_origin()
    {
        var angebotId = await SeedAngebotWithOneItemAsync();
        int catalogItemId;

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);
            var itemId = angebot.Sections.Single().Items.Single().Id;

            var catalogItem = CatalogItem.Create(
                "Fliesen verlegen",
                ItemUnit.SquareMeter(),
                Money.FromExact(82.25m),
                createdFromAngebotItemId: itemId);

            context.CatalogItems.Add(catalogItem);
            await context.SaveChangesAsync();
            catalogItemId = catalogItem.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);
            var section = angebot.Sections.Single();
            angebot.RemoveItem(section, section.Items.Single());
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.CatalogItems.SingleOrDefaultAsync(c => c.Id == catalogItemId);

        // BR-12: the shared library entry is never collateral damage of a draft edit. Cascade would
        // have deleted it, which is why cascade was never a candidate.
        Assert.NotNull(reloaded);
        Assert.Equal("Fliesen verlegen", reloaded.Title);
        Assert.False(reloaded.IsRetired);

        // The provenance link is the only casualty, and it is nullable precisely for this.
        Assert.Null(reloaded.CreatedFromAngebotItemId);
    }

    [Fact]
    public async Task Removing_the_whole_section_also_releases_a_contributed_line()
    {
        var angebotId = await SeedAngebotWithOneItemAsync();
        int catalogItemId;

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);

            var catalogItem = CatalogItem.Create(
                "Fliesen verlegen",
                ItemUnit.SquareMeter(),
                Money.FromExact(82.25m),
                createdFromAngebotItemId: angebot.Sections.Single().Items.Single().Id);

            context.CatalogItems.Add(catalogItem);
            await context.SaveChangesAsync();
            catalogItemId = catalogItem.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var angebot = await LoadAsync(context, angebotId);

            // Removing the section cascades to its items, so the same FK is exercised one level up.
            angebot.RemoveSection(angebot.Sections.Single());
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();

        Assert.Empty((await LoadAsync(readContext, angebotId)).Sections);
        Assert.NotNull(await readContext.CatalogItems.SingleOrDefaultAsync(c => c.Id == catalogItemId));
    }

    // ---- helpers ----

    private static Task<Angebot> LoadAsync(RenoTrackDbContext context, int angebotId) =>
        context.Angebote
            .Include(a => a.Sections)
            .ThenInclude(s => s.Items)
            .SingleAsync(a => a.Id == angebotId);

    /// <summary><c>Angebot.CreatedByInspectorId</c> is a real FK to <c>AspNetUsers</c> (Slice 15).</summary>
    private async Task<int> SeedApplicationUserAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Name = "Katalog Inspector" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedAngebotWithOneItemAsync()
    {
        var inspectorId = await SeedApplicationUserAsync();

        await using var context = fixture.CreateContext();

        var lead = Lead.Create("Kataloglink", "+49 151 4242", "kataloglink@example.de", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var angebot = Angebot.Create(lead.Id, inspectionId: null, $"ANG-LINK-{Guid.NewGuid():N}"[..18], createdByInspectorId: inspectorId);
        var section = angebot.AddSection("Bad", 1);
        angebot.AddItemToSection(
            section, "Fliesen verlegen", 13.5m, ItemUnit.SquareMeter(), Money.FromExact(82.25m), VatRate.Standard);

        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        return angebot.Id;
    }
}
