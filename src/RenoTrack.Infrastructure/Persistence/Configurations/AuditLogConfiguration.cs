using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// No FK to any business entity — ERD.md/Architecture.md §11 are explicit that AuditLog has "no
/// cross-entity linkage," only EntityType/EntityId, since one table logs against Lead,
/// Inspection, Angebot, and CatalogItem interchangeably (a real FK to any single table would be
/// wrong). PerformedByUserId has no FK yet, same reason as every other user-reference column
/// (Users table doesn't exist until the Identity slice, D44).
/// </summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired();

        // Same string-enum convention as Lead.Status/Angebot.Status (CLAUDE.md §21) — readable in raw SQL.
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(50);

        builder.Property(a => a.PerformedByUserId);
        builder.Property(a => a.Details).HasMaxLength(4000);
        builder.Property(a => a.CreatedAt).IsRequired();

        // ERD.md §3: "Fetching an entity's full history efficiently."
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
