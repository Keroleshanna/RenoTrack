using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.ValueConverters;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>InvoiceId</c> FK is a shadow property, configured from <see cref="InvoiceConfiguration"/>
/// — <see cref="Payment"/> has no visible C# FK property, exactly as <c>InspectionPhoto</c> and
/// <c>AngebotItem</c> have none. ERD.md lists the column because it exists in the database; the
/// Domain does not, because a child reaches its parent through the aggregate, not through an id.
///
/// <para>
/// <b><c>RecordedByAdminId</c> gets a real FK to <c>AspNetUsers</c></b>, per ERD.md and the
/// convention D44 settled for every other user-referencing column. <c>Restrict</c>, like all five
/// of them. Note this constraint proves only that the id names a real user — whether that user is
/// an active Admin is a business rule, and D62 puts business rules about staff accounts in the
/// Application layer via <c>IUserQueries</c>, not in a foreign key. Nothing in this slice decides
/// whether Phase 8 needs such a check; that belongs to the slice that builds the command.
/// </para>
/// <para>
/// <b><c>Amount</c> uses the established <c>MoneyConverter</c> at <c>decimal(18,2)</c></b>, the
/// same treatment every monetary column in this schema gets. It is always a copy of the Invoice's
/// own <c>GrossAmount</c> (Phase 8 is full-payment-only), so a narrower scale here would be able to
/// disagree with the invoice it settles.
/// </para>
/// <para>
/// <b><c>Method</c> is stored as a string</b> — ERD.md writes the column's domain out as
/// "BankTransfer | Cash | Other", the same way it writes every other enum-backed column, and the
/// stated reason is unchanged: readability in raw SQL during support work. Width 50 follows the
/// schema's non-status string-valued enums (<c>TokenLinks.EntityType</c>, <c>AngebotItems.Unit</c>)
/// rather than the 20 used for status columns.
/// </para>
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasConversion(new MoneyConverter())
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.Method).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.PaidAt).IsRequired();
        builder.Property(p => p.RecordedByAdminId).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.RecordedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
