using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mirrors <see cref="AuditLogConfiguration"/>, which is the closest existing precedent: an
/// Infrastructure-owned operational record with a polymorphic business reference and no foreign key.
/// </summary>
public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");
        builder.HasKey(d => d.Id);

        // Same string-enum convention as AuditLog.Action and every Domain status (CLAUDE.md §21) —
        // readable in raw SQL, and a new member costs no migration.
        builder.Property(d => d.NotificationType).IsRequired().HasConversion<string>().HasMaxLength(100);
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(50);

        builder.Property(d => d.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.EntityId).IsRequired();

        // Sized for a recipient *set*, not one address (S3-5) — three of the six notifications go to
        // the configured Admin list. Email:AdminRecipients is validated against this same constant at
        // startup, so an over-long list can never reach here. Nullable by design (S3-3): null means
        // delivery failed before a recipient could be resolved.
        builder.Property(d => d.Recipient).HasMaxLength(NotificationDelivery.MaxRecipientLength);

        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.LastAttemptAt);
        builder.Property(d => d.AttemptCount).IsRequired();
        builder.Property(d => d.SentAt);

        builder.Property(d => d.FailureType).HasMaxLength(200);
        builder.Property(d => d.FailureMessage).HasMaxLength(2000);

        // ERD.md §3: the Admin's "failed/pending notifications" read is a status filter, and is the
        // only list query on this table (PermissionMatrix.md §9).
        builder.HasIndex(d => d.Status);

        // ERD.md §3: "what happened to the notifications for this Angebot/Invoice/Lead" — the same
        // pair, for the same reason, as AuditLogs.
        builder.HasIndex(d => new { d.EntityType, d.EntityId });

        // No foreign key: EntityId points at Leads, Angebote or Invoices depending on EntityType, and
        // no single column can reference three tables (ERD.md, same as TokenLinks).
    }
}
