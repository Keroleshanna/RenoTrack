using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// The one table in this schema with deliberately no foreign key at all. Every other
/// "related by id only" Domain relationship still got a real FK constraint (CLAUDE.md §21 —
/// AngebotReviewComment.AngebotId, AngebotItem.CatalogItemId, and the five user-referencing
/// columns resolved in D44), because a database FK is an integrity mechanism independent of the
/// Domain's compile-time coupling. Here it is not merely deferred but impossible: EntityId points
/// at Angebote or Invoices depending on EntityType, and no single column can reference two tables.
/// ERD.md records the same conclusion ("polymorphic: EntityType + EntityId, no DB-level FK").
///
/// EntityType is stored as a string for the reason ERD.md gives for every other enum in this
/// schema — readability in raw SQL, which matters more here than anywhere else, since this is the
/// table an operator reads when investigating a customer's "my link doesn't work" report.
/// </summary>
public sealed class TokenLinkConfiguration : IEntityTypeConfiguration<TokenLink>
{
    public void Configure(EntityTypeBuilder<TokenLink> builder)
    {
        builder.ToTable("TokenLinks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.EntityType).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(t => t.EntityId).IsRequired();

        // 32 random bytes base64url-encode to 43 characters with the padding stripped. The column
        // is sized generously rather than pinned to exactly 43, so changing the token length later
        // is a code change and not a migration; it stays bounded so the unique index below is
        // buildable (SQL Server cannot index nvarchar(max)).
        builder.Property(t => t.Token).IsRequired().HasMaxLength(200);

        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.UsedAt);
        builder.Property(t => t.CreatedAt).IsRequired();

        // ERD.md §3: "Token (unique) — public token-link lookup is the hottest unauthenticated read
        // path". Unique for correctness as much as speed: two rows sharing a token would make that
        // lookup ambiguous, exactly as D60 reasoned for RefreshToken.TokenHash.
        builder.HasIndex(t => t.Token).IsUnique();
    }
}
