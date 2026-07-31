using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Unlike AuditLog's PerformedByUserId (deliberately not a FK, since one table logs against many
/// entity types), UserId here references exactly one table and AspNetUsers already exists as of
/// Slice 15 — so it gets a real FK, with DeleteBehavior.Restrict like every other relationship in
/// this schema (CLAUDE.md §21).
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        // SHA-256 rendered as hex is always exactly 64 characters. Fixed-length column, and a
        // unique index because two rows sharing a hash would make lookup ambiguous — and, since the
        // plaintext is 32 bytes of randomness, a genuine collision is not a real scenario; the
        // constraint exists to make a bug loud rather than silent.
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.RevokedAt);
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64).IsFixedLength();

        // Reuse detection revokes every outstanding token for one user, and that lookup is by
        // UserId alone — the only query in this table that isn't a point lookup by hash.
        builder.HasIndex(t => t.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
