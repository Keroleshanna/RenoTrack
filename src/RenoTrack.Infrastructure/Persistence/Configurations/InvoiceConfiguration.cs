using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// ERD.md's <c>PROJECT ||--o{ INVOICE : has</c> — one Project has many Invoices — so
/// <c>ProjectId</c> gets a real FK and is deliberately **not** unique, unlike
/// <c>Projects.AngebotId</c>. ERD.md §4 states the cardinality directly.
///
/// <para>
/// <b><c>InvoiceNumber</c> is unique (ERD.md §3, BR-9).</b> Unique for correctness before speed:
/// BR-9 forbids reusing a number, and two rows sharing one would make that rule unverifiable in
/// the only place it can ultimately be enforced. Sized <c>nvarchar(30)</c>, identical to
/// <c>Angebote.AngebotNumber</c> — same generator, same shape (<c>RE-YYYY-NNNNN</c>), so a
/// different width here would be an unexplained divergence rather than a decision.
/// </para>
/// <para>
/// <b>There is no <c>CreatedAt</c> column</b>, unlike every other aggregate root in this schema.
/// ERD.md's <c>INVOICE</c> defines none, and the Domain has none: <c>IssueDate</c> is set when the
/// Invoice comes into existence and is the business-meaningful timestamp. Adding a second one to
/// match the other tables would be inventing schema.
/// </para>
/// <para>
/// <b>The composite <c>(Status, DueDate)</c> index is ERD.md §3's</b>, whose stated purpose is the
/// overdue-detection check. It is created now, with the table, because that is where ERD.md puts
/// it — not because anything in Phase 8 runs that check on a schedule (nothing does; see
/// <c>Invoice.MarkOverdue</c>).
/// </para>
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProjectId).IsRequired();
        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(30);
        builder.Property(i => i.IssueDate).IsRequired();
        builder.Property(i => i.DueDate).IsRequired();

        // ERD.md: status enums stored as strings for readability in raw SQL during support work.
        // Width 20, matching every other Status column in this schema (Lead, Angebot, Project).
        builder.Property(i => i.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

        // All three via the established MoneyConverter at decimal(18,2), identical to
        // Angebot.NetTotal/GrossTotal and Project.AgreedTotal. A narrower scale would silently
        // round a legally-agreed figure — proven in Phase 7 Slice 2's adversarial run, where
        // decimal(18,0) stored 12345.67 as 12346.
        builder.Property(i => i.NetAmount)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(i => i.VatAmount)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(i => i.GrossAmount)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // Nullable per ERD.md — only a voided Invoice carries one. Width 4000 follows
        // AngebotReviewComments.Comment, the schema's existing staff-authored free-text column.
        builder.Property(i => i.VoidReason).HasMaxLength(4000);

        // By id only, no navigation property on either side (CLAUDE.md §2). Restrict, like every
        // other reference between independent aggregates in this schema.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.InvoiceNumber).IsUnique();
        builder.HasIndex(i => new { i.Status, i.DueDate });

        // Invoice.Payments is IReadOnlyList<Payment> over a private List<T> field with no public
        // setter, so EF must be told to materialize through the field directly. Cascade — not
        // Restrict — because this is aggregate composition rather than a reference between
        // independent aggregates: a Payment has no meaning apart from its Invoice. Identical to
        // Angebot→Sections, AngebotSection→Items and Inspection→Photos. IsRequired() is explicit
        // because without it EF defaults the shadow FK to nullable, which D46 caught as a real bug
        // in the first generated migration rather than by inspection.
        builder.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey("InvoiceId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
