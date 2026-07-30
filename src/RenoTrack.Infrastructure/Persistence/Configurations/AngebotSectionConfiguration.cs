using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Subtotal is deliberately NOT mapped to a column. ERD.md originally listed it as a cached
/// column, but the actual Domain code (AngebotSection.Subtotal) is a pure `=>` computed
/// property with no backing field — CLAUDE.md §2 confirms this is intentional ("never stored
/// fields... No ERD-stated performance reason applies at that granularity"). The Domain code
/// and CLAUDE.md are authoritative here; ERD.md is corrected in this same commit.
/// </summary>
public sealed class AngebotSectionConfiguration : IEntityTypeConfiguration<AngebotSection>
{
    public void Configure(EntityTypeBuilder<AngebotSection> builder)
    {
        builder.ToTable("AngebotSections");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).IsRequired().HasMaxLength(200);
        builder.Property(s => s.SortOrder).IsRequired();

        builder.Ignore(s => s.Subtotal);

        // AngebotSection.Items is IReadOnlyList<AngebotItem> over a private List<T> field.
        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey("SectionId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
