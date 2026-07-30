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

        builder.HasOne<AngebotItem>()
            .WithMany()
            .HasForeignKey(c => c.CreatedFromAngebotItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
