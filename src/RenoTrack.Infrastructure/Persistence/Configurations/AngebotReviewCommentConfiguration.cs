using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// AngebotId gets a real FK to Angebote (that table exists today) even though AngebotReviewComment
/// is a genuinely independent aggregate with no C# navigation to Angebot (D32) — the FK is a
/// database-integrity concern, separate from the Domain's "related by id only" design.
/// AdminUserId gets a real FK to AspNetUsers as of Slice 15 (D44 resolved) — required (int),
/// matching the already-correct Domain property.
/// </summary>
public sealed class AngebotReviewCommentConfiguration : IEntityTypeConfiguration<AngebotReviewComment>
{
    public void Configure(EntityTypeBuilder<AngebotReviewComment> builder)
    {
        builder.ToTable("AngebotReviewComments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.AngebotId).IsRequired();
        builder.Property(c => c.AdminUserId).IsRequired();
        builder.Property(c => c.Comment).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasOne<Angebot>()
            .WithMany()
            .HasForeignKey(c => c.AngebotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.AdminUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
