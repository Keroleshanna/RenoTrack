using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// One DbSet per aggregate root only — AngebotSection/AngebotItem/InspectionPhoto have no
/// DbSet of their own, reachable only through their aggregate root's navigation, matching
/// CLAUDE.md §2's "aggregate roots are the only public entry point" rule extended to how the
/// persistence layer is queried. AuditLogs/NumberSequences are the first DbSets with no
/// Domain-entity counterpart (D49, D51); AspNetUsers/AspNetRoles/etc. (via IdentityDbContext,
/// Slice 15) are the second such group — required to inherit from IdentityUser/IdentityRole, so
/// they cannot live in Domain either (D53). No `NumberSequence`/`AuditLog`/Identity DbSet was
/// added speculatively — each arrived only in the slice that actually needed it.
/// </summary>
public sealed class RenoTrackDbContext(DbContextOptions<RenoTrackDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Angebot> Angebote => Set<Angebot>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<AngebotReviewComment> AngebotReviewComments => Set<AngebotReviewComment>();
    public DbSet<TokenLink> TokenLinks => Set<TokenLink>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Must run first — establishes AspNetUsers/AspNetRoles/etc. before our own
        // configurations (some of which now add FKs pointing at ApplicationUser).
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RenoTrackDbContext).Assembly);
    }
}
