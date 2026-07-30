using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// AssignedInspectorId gets a real FK to AspNetUsers as of Slice 15 (D44's deferral resolved —
/// the Users table now exists). Nullable (int?), matching the already-correct Domain property —
/// a Lead may have no assigned Inspector yet, before BR-13's ScheduleInspection auto-assignment.
/// </summary>
public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("Leads");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Phone).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Email).IsRequired().HasMaxLength(320);
        builder.Property(l => l.Address).HasMaxLength(500);
        builder.Property(l => l.Notes).HasMaxLength(2000);

        // ERD.md: Source/Status stored as string enums for readability in raw SQL during support/debugging.
        builder.Property(l => l.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(30);

        builder.Property(l => l.CreatedAt).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.AssignedInspectorId)
            .OnDelete(DeleteBehavior.Restrict);

        // ERD.md §3: pipeline filtering (SRS FR-2.4).
        builder.HasIndex(l => new { l.Status, l.AssignedInspectorId });
    }
}
