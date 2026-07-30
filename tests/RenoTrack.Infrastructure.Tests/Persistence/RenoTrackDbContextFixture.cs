using Microsoft.EntityFrameworkCore;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Shared across every persistence test class via [Collection("Infrastructure Database")], so
/// the schema is created once per test run rather than once per class, and so xUnit runs every
/// test touching this database serially (collections never parallelize against each other) —
/// avoiding interference between tests sharing one real LocalDB instance. Real SQL Server
/// LocalDB, deliberately not the EF Core InMemory provider, per the standing decision that
/// InMemory wouldn't enforce the real constraints/types this phase needs to verify.
/// </summary>
public sealed class RenoTrackDbContextFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackInfrastructureTests;Trusted_Connection=True;TrustServerCertificate=True";

    public RenoTrackDbContext CreateContext()
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
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("Infrastructure Database")]
public sealed class InfrastructureDatabaseCollection : ICollectionFixture<RenoTrackDbContextFixture>;
