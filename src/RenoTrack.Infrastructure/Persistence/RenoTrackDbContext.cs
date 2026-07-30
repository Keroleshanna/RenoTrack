using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// One DbSet per aggregate root only — AngebotSection/AngebotItem/InspectionPhoto have no
/// DbSet of their own, reachable only through their aggregate root's navigation, matching
/// CLAUDE.md §2's "aggregate roots are the only public entry point" rule extended to how the
/// persistence layer is queried. Identity tables and NumberSequence/AuditLog are added in
/// their own later slices, not speculatively here (CLAUDE.md §4's growth-on-demand discipline
/// applied to the DbContext itself).
/// </summary>
public sealed class RenoTrackDbContext(DbContextOptions<RenoTrackDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Angebot> Angebote => Set<Angebot>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<AngebotReviewComment> AngebotReviewComments => Set<AngebotReviewComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RenoTrackDbContext).Assembly);
    }
}
