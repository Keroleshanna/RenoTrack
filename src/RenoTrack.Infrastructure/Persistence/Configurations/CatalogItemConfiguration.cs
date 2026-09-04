using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// CreatedFromAngebotItemId gets a real FK to AngebotItems — the reverse trace link (BR-8's
/// other direction). Restrict, never cascade, for the same reason as AngebotItem.CatalogItemId's
/// FK: neither side of this mutual reference is ever hard-deleted, so cascade behavior is moot,
/// but Restrict is the safe default regardless.
/// </summary>
public sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title).IsRequired().HasMaxLength(300);
        builder.Property(c => c.DefaultSpecification).HasMaxLength(4000);

        builder.Property(c => c.DefaultUnit)
            .HasConversion(new ItemUnitConverter())
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.SuggestedUnitPrice)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(c => c.CreatedFromAngebotItemId);
        builder.Property(c => c.IsRetired).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // SetNull, not Restrict. `CreatedFromAngebotItemId` is a provenance trace (BR-8) and nothing
        // branches on it, so it must never be able to veto an unrelated operation — and under
        // Restrict it did exactly that: removing a draft line that had been saved to the Catalog
        // failed with a DbUpdateException on the FK, surfacing as an unmapped 500.
        //
        // That contradicted two rules at once. CLAUDE.md §2 states plainly that removing a line from
        // a Draft/ChangesRequested Angebot is legitimate — it is unsent working material, not a
        // record. And BR-12 keeps the *Catalog* entry alive; it says nothing about the draft line the
        // entry was once copied from, which BR-8's copy-on-create semantics make independent the
        // instant it exists.
        //
        // So the line goes, the Catalog entry stays, and its trace link becomes NULL — which the
        // column already permits and which is the honest record of "this entry no longer has a
        // surviving origin". Cascade was never a candidate: it would delete a shared library entry
        // (BR-12), and Restrict's only other alternative was refusing the removal, which invents a
        // rule no document states.
        builder.HasOne<AngebotItem>()
            .WithMany()
            .HasForeignKey(c => c.CreatedFromAngebotItemId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
