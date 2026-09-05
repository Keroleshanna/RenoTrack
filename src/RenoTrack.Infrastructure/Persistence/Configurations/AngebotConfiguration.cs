using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// CreatedByInspectorId/ReviewedByAdminId get real FKs to AspNetUsers as of Slice 15 (D44
/// resolved) — CreatedByInspectorId required (int), ReviewedByAdminId nullable (int?, only set
/// once Approve/RequestChanges is called), both matching the already-correct Domain properties.
/// LeadId and InspectionId already had real FKs since Slice 2. VatBreakdown is ignored: no ERD
/// column exists for it at all (Architecture.md §6.1 — always computed, variable-shaped, nothing
/// to denormalize).
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

        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(a => a.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Inspection>()
            .WithMany()
            .HasForeignKey(a => a.InspectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.CreatedByInspectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.ReviewedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
        builder.HasIndex(a => a.Status);

        builder.Property(a => a.CreatedByInspectorId).IsRequired();
        builder.Property(a => a.ReviewedByAdminId);
        builder.Property(a => a.SentAt);
        builder.Property(a => a.DecisionAt);

        // FR-6.3's optional rejection reason (D98, migration #12). Nullable with no default and no
        // sentinel: NULL means "not given", which is true of every historical rejection and of any
        // future one the customer leaves blank. The length matches
        // Angebot.MaxDecisionReasonLength rather than repeating 1000, so the column and the
        // aggregate's own guard cannot drift apart.
        builder.Property(a => a.DecisionReason).HasMaxLength(Angebot.MaxDecisionReasonLength);
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
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Sections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
