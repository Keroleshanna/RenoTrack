using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Unique constraint on (SequenceType, Year) is what NumberGeneratorService's raw SQL upsert
/// relies on for its first-of-year race handling (D52) — a concurrent duplicate INSERT fails
/// against this constraint, which is the signal to retry the UPDATE once.
/// </summary>
public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("NumberSequences");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.SequenceType).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Year).IsRequired();
        builder.Property(n => n.LastValue).IsRequired();

        builder.HasIndex(n => new { n.SequenceType, n.Year }).IsUnique();
    }
}
