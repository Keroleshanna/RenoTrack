using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// InspectorId gets a real FK to AspNetUsers as of Slice 15 (D44 resolved). Required (int),
/// matching the already-correct Domain property — every Inspection always has an Inspector from
/// creation (Inspection.Schedule).
/// </summary>
public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("Inspections");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.LeadId).IsRequired();
        builder.Property(i => i.InspectorId).IsRequired();
        builder.Property(i => i.ScheduledAt).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.CompletedAt);

        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(i => i.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(i => i.InspectorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Inspection.Photos is IReadOnlyList<InspectionPhoto> over a private List<T> backing
        // field, no public setter — EF must be told to materialize through the field directly.
        builder.HasMany(i => i.Photos)
            .WithOne()
            .HasForeignKey("InspectionId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
