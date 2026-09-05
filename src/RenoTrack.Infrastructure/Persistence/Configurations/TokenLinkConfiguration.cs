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

        // An optimistic-concurrency token as well as a required column (D99). ExpiresAt is the
        // column a re-issue writes, so it is the column that can gate one re-issue against another:
        // both readers see the same original value, the first commits, the second's UPDATE matches
        // zero rows, and EF rolls its whole batch back — taking the replacement link it had already
        // staged with it, which is what keeps "at most one usable credential" true.
        //
        // UsedAt (below) gates a different race — a customer decision against a re-issue — because
        // the decision is what writes UsedAt. Neither token protects the other's race: a guarantee
        // comes from the column the operation actually writes, never from a token being present on
        // the row. The first design of this slice got that wrong and it was caught in review.
        //
        // No schema change: a non-rowversion token is client-side WHERE-clause behaviour. It is
        // recorded in the model snapshot, so migration #13 exists with legitimately empty Up/Down —
        // the same situation as migration #11.
        builder.Property(t => t.ExpiresAt).IsRequired().IsConcurrencyToken();

        // Optimistic concurrency, and the whole reason BR-4 is enforceable rather than merely
        // intended (D96). UsedAt is the only column any code path ever mutates on this table
        // (TokenLink.MarkUsed is the aggregate's one mutator), so making it the concurrency token
        // costs nothing anywhere else and turns the decision UPDATE into
        // "UPDATE TokenLinks SET UsedAt = @now WHERE Id = @id AND UsedAt IS NULL".
        //
        // Without it, two simultaneous decisions on the same link both read UsedAt as null, both
        // pass MarkUsed()'s in-memory guard, and both commit — consuming the link twice, writing
        // two audit rows, sending two Admin emails, and (when the two callers chose differently)
        // leaving Angebot and Lead in states that contradict each other, since neither of those
        // rows carries a token of its own. With it, the loser's UPDATE matches no row, EF Core
        // throws DbUpdateConcurrencyException, and its *entire* batch is rolled back — so the
        // Angebot and Lead writes it had queued never land either. That batch-level atomicity is
        // what makes a token on this one column sufficient to protect all three aggregates.
        //
        // The same shape as RefreshToken.RevokedAt (D60), for the same reason: a nullable
        // "consumed at" timestamp is a natural compare-and-set, needing no rowversion column and
        // therefore no schema change.
        builder.Property(t => t.UsedAt).IsConcurrencyToken();

        builder.Property(t => t.CreatedAt).IsRequired();

        // ERD.md §3: "Token (unique) — public token-link lookup is the hottest unauthenticated read
        // path". Unique for correctness as much as speed: two rows sharing a token would make that
        // lookup ambiguous, exactly as D60 reasoned for RefreshToken.TokenHash.
        builder.HasIndex(t => t.Token).IsUnique();
    }
}
