using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The database bootstrap policy of D63, proven against a real database rather than by observing
/// that a method was called.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here inspects actual database state — rows in <c>__EFMigrationsHistory</c>, rows
/// in <c>AspNetRoles</c> — or the refusal that state produces. A test that only checked
/// "<c>MigrateAsync</c> was invoked" would still pass against a database that failed to migrate.
/// </para>
/// <para>
/// Uses its own throwaway database, following <see cref="InitialCreateMigrationTests"/>'s pattern,
/// so it never interferes with the shared "Infrastructure Database" collection. It deliberately does
/// <b>not</b> join that collection: those tests share an <c>EnsureCreated</c> schema, and these need
/// to create and destroy migration history repeatedly.
/// </para>
/// <para>
/// <b>D40 and D58 are untouched.</b> This class builds its own container rather than booting the
/// application, and neither test project's existing database lifecycle is routed through
/// <see cref="DatabaseInitializer"/>.
/// </para>
/// </remarks>
public sealed class DatabaseInitializerTests : IAsyncLifetime
{
    private const string DatabaseName = "RenoTrackInitializerTests";

    private const string ConnectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";

    public async Task InitializeAsync() => await DropDatabaseAsync();

    public async Task DisposeAsync() => await DropDatabaseAsync();

    private static async Task DropDatabaseAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
    }

    private static RenoTrackDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<RenoTrackDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>
    /// Builds the real Infrastructure container the same way <c>Program.cs</c> does, so the
    /// registrations under test are the production ones rather than a hand-assembled substitute
    /// (the duplication hazard the Phase 3 review flagged).
    /// </summary>
    private static ServiceProvider BuildProvider(DatabaseInitializationMode? mode, string environmentName)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:RenoTrackDb"] = ConnectionString,
            [$"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.RootPath)}"] =
                Path.Combine(Path.GetTempPath(), "RenoTrackInitializerTests"),
        };

        // Left absent entirely when null — that is how the "omitted means Verify" default is tested.
        if (mode is not null)
        {
            settings[DatabaseInitializationOptions.ModeKey] = mode.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    // environmentName defaults via null rather than a parameter default: Environments.Development is
    // a static readonly field, not a compile-time constant.
    private static async Task RunInitializerAsync(DatabaseInitializationMode? mode, string? environmentName = null)
    {
        await using var provider = BuildProvider(mode, environmentName ?? Environments.Development);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    // ---- Bootstrap from zero -----------------------------------------------

    /// <summary>
    /// The state-based bootstrap proof: a completely empty database, initialized once, ends up with
    /// every migration recorded as applied and both roles present. Skipping the actual migration
    /// application makes this fail on the history query, not on a mocked expectation.
    /// </summary>
    [Fact]
    public async Task Migrate_bootstraps_a_completely_empty_database_to_a_ready_state()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        await using var context = CreateDbContext();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var known = context.Database.GetMigrations().ToList();

        Assert.NotEmpty(known);
        Assert.Equal(known.OrderBy(m => m), applied.OrderBy(m => m));

        Assert.Equal(
            IdentityRoleSeeder.RoleNames.OrderBy(r => r),
            await RoleNamesInDatabaseAsync());
    }

    /// <summary>
    /// Explicitly required: running Development initialization twice must be safe. The second run
    /// re-applies nothing and must not duplicate role rows.
    /// </summary>
    [Fact]
    public async Task Migrate_is_idempotent_across_repeated_runs()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        Assert.Equal(
            IdentityRoleSeeder.RoleNames.OrderBy(r => r),
            await RoleNamesInDatabaseAsync());
    }

    [Fact]
    public async Task Verify_succeeds_against_a_fully_initialized_database()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        // No exception is the assertion.
        await RunInitializerAsync(DatabaseInitializationMode.Verify);
    }

    // ---- Migration-history compatibility, both directions independently ----

    /// <summary>Database behind the application: a known migration is not recorded as applied.</summary>
    [Fact]
    public async Task Verify_refuses_when_a_required_migration_is_missing()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        string removed;
        await using (var context = CreateDbContext())
        {
            removed = (await context.Database.GetAppliedMigrationsAsync()).Order(StringComparer.Ordinal).Last();
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0}", removed);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Verify));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(removed, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Database ahead of the application: an applied migration id this build does not know about.
    /// This is the direction <c>GetPendingMigrations</c> alone cannot detect — the realistic cause is
    /// a newer release being rolled back to this one, leaving the schema newer than the code.
    /// </summary>
    [Fact]
    public async Task Verify_refuses_when_the_database_has_an_unknown_applied_migration()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        const string unknownMigrationId = "29990101000000_FromAFutureRelease";

        await using (var context = CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ({0}, {1})",
                unknownMigrationId,
                "10.0.10");
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Verify));

        Assert.Contains(unknownMigrationId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Required roles ----------------------------------------------------

    [Fact]
    public async Task Verify_refuses_when_a_required_role_is_missing()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        await using (var context = CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [AspNetRoles] WHERE [Name] = {0}", IdentityRoleSeeder.InspectorRole);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Verify));

        Assert.Contains(IdentityRoleSeeder.InspectorRole, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verify never writes, so it must not "fix" what it finds — otherwise the runtime login would
    /// need write permission and the refusal above would be self-healing rather than a real gate.
    /// </summary>
    [Fact]
    public async Task Verify_does_not_repair_what_it_finds()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        await using (var context = CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [AspNetRoles] WHERE [Name] = {0}", IdentityRoleSeeder.InspectorRole);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Verify));

        Assert.DoesNotContain(IdentityRoleSeeder.InspectorRole, await RoleNamesInDatabaseAsync());
    }

    // ---- Environment policy ------------------------------------------------

    /// <summary>
    /// The Production guard is a hard refusal, and it must fire <b>before</b> anything is written —
    /// asserted by proving the database was never even created.
    /// </summary>
    [Fact]
    public async Task Migrate_is_refused_in_production_and_writes_nothing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Migrate, Environments.Production));

        Assert.Contains(DatabaseInitializationOptions.ModeKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);

        await using var context = CreateDbContext();
        Assert.False(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Verify_is_permitted_in_production()
    {
        await RunInitializerAsync(DatabaseInitializationMode.Migrate);

        await RunInitializerAsync(DatabaseInitializationMode.Verify, Environments.Production);
    }

    // ---- Configuration -----------------------------------------------------

    /// <summary>An omitted mode must mean Verify — the read-only path — never the writing one.</summary>
    [Fact]
    public async Task An_omitted_mode_defaults_to_verify_and_therefore_writes_nothing()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(mode: null));

        // It verified rather than migrated: the failure is about readiness, not about configuration.
        Assert.Contains("Cannot connect", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var context = CreateDbContext();
        Assert.False(await context.Database.CanConnectAsync());
    }

    [Fact]
    public void An_unrecognised_mode_fails_eagerly_and_names_the_key_and_allowed_values()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseInitializationOptions.ModeKey] = "MigrateIfYouFeelLikeIt",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseInitializationOptions.FromConfiguration(configuration));

        Assert.Contains(DatabaseInitializationOptions.ModeKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseInitializationMode.Verify), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseInitializationMode.Migrate), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A database that does not exist reports SQL error 4060, which EF Core wraps in a message
    /// recommending <c>EnableRetryOnFailure</c> — pointing an operator at retry policies rather than
    /// at the missing database. This pins the clearer diagnostic that replaces it.
    /// </summary>
    [Fact]
    public async Task Verify_against_a_non_existent_database_names_the_connection_not_a_migration_fault()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunInitializerAsync(DatabaseInitializationMode.Verify));

        Assert.Contains("Cannot connect", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__EFMigrationsHistory", exception.Message, StringComparison.Ordinal);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<List<string>> RoleNamesInDatabaseAsync()
    {
        await using var provider = BuildProvider(DatabaseInitializationMode.Verify, Environments.Development);
        using var scope = provider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

        return await roleManager.Roles
            .Select(r => r.Name!)
            .OrderBy(n => n)
            .ToListAsync();
    }
}
