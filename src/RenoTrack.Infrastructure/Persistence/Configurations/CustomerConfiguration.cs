using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// ERD.md: "One Customer per Lead — created at Project-conversion time", which is what the unique
/// index on <c>LeadId</c> enforces — the diagram's <c>LEAD ||--o| CUSTOMER</c> cardinality stated
/// as a constraint rather than left to handler discipline. The Application layer still checks it
/// first (D62: a database constraint is a mechanism, not a business rule), so an ordinary repeat
/// conversion is a 409 rather than an unmapped 500.
///
/// <para>
/// <b>String lengths match <c>LeadConfiguration</c>'s exactly</b> (Name 200, Email 320, Phone 50,
/// Address 500). ERD.md specifies no lengths for any table, so the meaningful constraint is that
/// these columns hold values copied verbatim from a Lead — a narrower column here would reject at
/// conversion time a value the Lead row already accepted.
/// </para>
/// <para>
/// <b><c>Address</c> is nullable</b>, matching the Domain property and ERD.md's corrected diagram.
/// The public contact form does not collect an address, so a required column here would make
/// conversion of a website-sourced Lead impossible — see `PHASE7_PROGRESS.md`, decision 1.
/// </para>
/// </summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.LeadId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Email).IsRequired().HasMaxLength(320);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Address).HasMaxLength(500);

        // No navigation property on either side — Customer relates to its Lead by id only
        // (CLAUDE.md §2), so the relationship is declared with the generic HasOne<Lead>() overload
        // exactly as LeadConfiguration declares its AspNetUsers FK. Restrict throughout: nothing in
        // this schema is ever hard-deleted, so cascade would never fire, and Restrict is the
        // correct safe default regardless (CLAUDE.md §21).
        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(c => c.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.LeadId).IsUnique();
    }
}
