using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// CreatedByInspectorId/ReviewedByAdminId have no FK constraint yet — Users table doesn't exist
/// until the Identity slice. VatBreakdown is ignored: no ERD column exists for it at all
/// (Architecture.md §6.1 — always computed, variable-shaped, nothing to denormalize).
/// </summary>
public sealed class AngebotConfiguration : IEntityTypeConfiguration<Angebot>
{
    public void Configure(EntityTypeBuilder<Angebot> builder)
    {
        builder.ToTable("Angebote");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.LeadId).IsRequired();
        builder.Property(a => a.InspectionId);
        builder.Property(a => a.AngebotNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(a => a.AngebotNumber).IsUnique();

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(a => a.Status);

        builder.Property(a => a.CreatedByInspectorId).IsRequired();
        builder.Property(a => a.ReviewedByAdminId);
        builder.Property(a => a.SentAt);
        builder.Property(a => a.DecisionAt);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.Property(a => a.NetTotal)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();
        builder.Property(a => a.GrossTotal)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Ignore(a => a.VatBreakdown);

        // Angebot.Sections is IReadOnlyList<AngebotSection> over a private List<T> field.
        builder.HasMany(a => a.Sections)
            .WithOne()
            .HasForeignKey("AngebotId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Sections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
