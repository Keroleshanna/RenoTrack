using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// LineTotal is deliberately NOT mapped — same reasoning as AngebotSection.Subtotal (a pure
/// computed property, no backing field; ERD.md corrected in this same commit). CatalogItemId
/// gets a real FK to CatalogItems (both tables exist today, unlike the Users-referencing
/// columns elsewhere) — BR-8's "no live reference" is about Domain behavior (AngebotItem never
/// re-reads CatalogItem after creation), not about whether the database enforces that the
/// traceability pointer is valid; the two are independent. Restrict, never cascade: a
/// CatalogItem is never hard-deleted (BR-12), so this constraint is never actually exercised
/// by a delete, but it protects data integrity at insert time regardless. The SectionId FK is
/// a shadow property (configured from AngebotSectionConfiguration) — AngebotItem has no visible
/// C# FK property to it.
/// </summary>
public sealed class AngebotItemConfiguration : IEntityTypeConfiguration<AngebotItem>
{
    public void Configure(EntityTypeBuilder<AngebotItem> builder)
    {
        builder.ToTable("AngebotItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.CatalogItemId);
        builder.Property(i => i.Description).IsRequired().HasMaxLength(500);
        builder.Property(i => i.Specification).HasMaxLength(4000);
        builder.Property(i => i.Quantity).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(i => i.Unit)
            .HasConversion(new ItemUnitConverter())
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // Int-backed enum (Zero=0, Reduced=7, Sixteen=16, Standard=19) — the underlying values
        // already are the percentages, so the default EF enum-to-int mapping needs no converter.
        builder.Property(i => i.VatRate).IsRequired();

        builder.Ignore(i => i.LineTotal);

        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(i => i.CatalogItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
