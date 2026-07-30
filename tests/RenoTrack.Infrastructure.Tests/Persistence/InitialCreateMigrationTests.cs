using Microsoft.EntityFrameworkCore;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Verifies the InitialCreate migration itself — as opposed to RenoTrackDbContextFixture's
/// EnsureCreated-based tests, which verify the model but never exercise the actual migration
/// script. Uses its own database name so it doesn't interfere with the shared
/// "Infrastructure Database" collection's EnsureCreated-based schema.
/// </summary>
public sealed class InitialCreateMigrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackMigrationTest;Trusted_Connection=True;TrustServerCertificate=True";

    private RenoTrackDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RenoTrackDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new RenoTrackDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task InitialCreateMigration_AppliesCleanlyToAFreshDatabase()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, m => m.EndsWith("_InitialCreate"));
    }

    [Fact]
    public async Task InitialCreateMigration_ProducesASchemaThatMatchesTheCurrentModel()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // If the migration's schema drifted from the current EF model, this would be non-empty —
        // the exact "does the migration match the model" check EF itself uses.
        var pendingModelChanges = context.Database.HasPendingModelChanges();
        Assert.False(pendingModelChanges);
    }
}
