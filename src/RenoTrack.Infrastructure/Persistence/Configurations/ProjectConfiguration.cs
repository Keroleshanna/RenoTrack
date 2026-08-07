using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// ERD.md's <c>ANGEBOT ||--o| PROJECT : "converts to"</c> — one Angebot becomes at most one
/// Project — is enforced by the unique index on <c>AngebotId</c>. As with
/// <c>Customers.LeadId</c>, the Application layer checks it first so a second conversion attempt
/// is a 409 rather than an unmapped `DbUpdateException` (D62); the index is the mechanism that
/// makes the rule true even if a future caller forgets.
///
/// <para>
/// <b><c>CustomerId</c> is deliberately not unique</b> — ERD.md §4: "one Customer can have many
/// Projects". That the current <c>Customers.LeadId UK</c> design makes repeat customers
/// unreachable is a recorded limitation, not a reason to tighten this column: doing so would bake
/// the limitation into the schema and make resolving it later a breaking change.
/// </para>
/// <para>
/// <b><c>AgreedTotal</c> uses the established <c>MoneyConverter</c> and <c>decimal(18,2)</c></b>,
/// identical to <c>Angebot.NetTotal</c>/<c>GrossTotal</c> — it is a snapshot of
/// <c>Angebot.GrossTotal</c>, so any narrower or differently-rounded column type could silently
/// alter the very value the snapshot exists to preserve.
/// </para>
/// </summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.CustomerId).IsRequired();
        builder.Property(p => p.AngebotId).IsRequired();

        // ERD.md: status enums stored as strings for readability in raw SQL during support work.
        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

        builder.Property(p => p.AgreedTotal)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CompletedAt);

        // Both relationships are by id only, with no navigation property on either side
        // (CLAUDE.md §2). Restrict on both, consistent with every other FK in this schema.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Angebot>()
            .WithMany()
            .HasForeignKey(p => p.AngebotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.AngebotId).IsUnique();
    }
}
