using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// InspectorId has no FK constraint yet — same reason as Lead.AssignedInspectorId (Users table
/// doesn't exist until the Identity slice).
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

        // Inspection.Photos is IReadOnlyList<InspectionPhoto> over a private List<T> backing
        // field, no public setter — EF must be told to materialize through the field directly.
        builder.HasMany(i => i.Photos)
            .WithOne()
            .HasForeignKey("InspectionId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
