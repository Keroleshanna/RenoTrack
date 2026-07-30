using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// AssignedInspectorId has no FK constraint yet — the Users table doesn't exist until the
/// Identity slice (Phase 3, Slice 15). Adding the constraint then is a follow-up migration,
/// not a change to this configuration's shape.
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

        // ERD.md §3: pipeline filtering (SRS FR-2.4).
        builder.HasIndex(l => new { l.Status, l.AssignedInspectorId });
    }
}
