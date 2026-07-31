using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests;

/// <summary>
/// Boots the real RenoTrack.Api application in-process (the actual Program.cs, not a hand-rolled
/// approximation of it) so tests exercise the genuine HTTP pipeline: routing, model binding,
/// authorization, and error handling. Real SQL Server LocalDB backs it, never the EF Core InMemory
/// provider — the same standing decision that governs RenoTrack.Infrastructure.Tests (D40),
/// applied here for the additional reason that Identity's UserManager/password hashing needs a
/// real store to be meaningful.
/// </summary>
/// <remarks>
/// The schema is created with <c>Database.MigrateAsync()</c>, deliberately not
/// <c>EnsureCreatedAsync()</c> (which RenoTrack.Infrastructure.Tests' own fixture uses). The two
/// projects have different responsibilities: Infrastructure.Tests constructs a DbContext directly
/// and never runs Program.cs, so it has no production startup path to stay faithful to. This
/// project boots the real application, which in production will always run against a migrated
/// database. Migrating here also keeps the fixture correct under either outcome of the still-open
/// migration-application decision (Phase 4's final slice): if startup migrates, this call is an
/// idempotent no-op; if CI/CD migrates, this call faithfully plays that role. EnsureCreated would
/// break under the former, since it never writes __EFMigrationsHistory.
/// </remarks>
public sealed class RenoTrackApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Deliberately a different database from RenoTrack.Infrastructure.Tests' so the two suites can
    /// never interfere, including when their test processes run concurrently.
    /// </summary>
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackApiTests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development is what the real Program.cs gates MapOpenApi/MapScalarApiReference behind,
        // and WebApplicationFactory would otherwise default the host to Production.
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:RenoTrackDb", ConnectionString);
    }

    public async Task InitializeAsync()
    {
        // The schema must exist before the host is first created: Program.cs seeds Identity roles
        // during startup, which fails against a database with no tables.
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await using (var context = CreateDbContext())
        {
            await context.Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    private static RenoTrackDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RenoTrackDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new RenoTrackDbContext(options);
    }
}

/// <summary>
/// Every API test class shares one factory and one database via this collection, so the schema is
/// built once per run and xUnit runs these tests serially rather than in parallel against the same
/// LocalDB instance — mirroring RenoTrack.Infrastructure.Tests' collection fixture.
/// </summary>
[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<RenoTrackApiFactory>;
