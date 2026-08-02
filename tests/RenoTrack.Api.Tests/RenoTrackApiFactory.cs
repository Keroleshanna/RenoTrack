using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
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

        // Supplied here, never read from appsettings.Development.json — that file is gitignored
        // (it holds a real signing key), so depending on it would make these tests pass locally and
        // fail in CI's fresh clone, where AddJwtAuthentication would throw at startup. Test
        // configuration must come from the test project itself.
        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}", TestIssuer);
        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}", TestAudience);
        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}", TestSigningKey);

        // Each run gets its own storage root, deleted on teardown, so uploaded files never leak
        // between runs and a test can assert on the real directory's contents.
        builder.UseSetting($"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.RootPath)}", StorageRoot);

        // Migrate, because this fixture needs the Identity roles seeded and, as of D63, normal
        // startup no longer seeds them — Verify would fail on a database that has never been
        // initialized. This is a real requirement of the harness, not routing the test lifecycle
        // through the production initializer for consistency's sake: D58 is unchanged, and
        // InitializeAsync below still owns this database's lifetime with its own
        // EnsureDeleted/Migrate. The host is Development here, so the Production guard does not
        // apply; that guard is proven separately, against a Production host, in
        // DatabaseInitializerTests.
        builder.UseSetting(
            DatabaseInitializationOptions.ModeKey,
            nameof(DatabaseInitializationMode.Migrate));
    }

    /// <summary>
    /// Where <see cref="LocalDiskFileStorage"/> writes during this test run. Unique per factory
    /// instance so a parallel run of another suite cannot collide with it.
    /// </summary>
    public string StorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "RenoTrackApiTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Test-only JWT settings. <see cref="TestSigningKey"/> is what
    /// <c>AuthenticationTests</c> signs its deliberately-expired token with, so that token is
    /// rejected for expiry rather than for a bad signature — which is the only way that test
    /// proves what it claims to.
    /// </summary>
    public const string TestIssuer = "RenoTrack.Api.Tests";
    public const string TestAudience = "RenoTrack.Api.Tests";
    public const string TestSigningKey = "api-tests-signing-key-long-enough-for-hmac-sha256";

    /// <summary>Known-password test users, seeded once per run by <see cref="InitializeAsync"/>.</summary>
    public const string AdminEmail = "admin@renotrack.test";
    public const string AdminPassword = "Admin#Pass123";
    public const string InspectorEmail = "inspector@renotrack.test";
    public const string InspectorPassword = "Inspector#Pass123";

    /// <summary>Seeded with <c>IsActive = false</c> — proves a deactivated account cannot log in.</summary>
    public const string InactiveEmail = "inactive@renotrack.test";
    public const string InactivePassword = "Inactive#Pass123";

    /// <summary>
    /// Dedicated to the lockout test so repeated deliberate failures cannot leave another test's
    /// user locked out — these tests share one database and run serially.
    /// </summary>
    public const string LockoutEmail = "lockout@renotrack.test";
    public const string LockoutPassword = "Lockout#Pass123";

    /// <summary>
    /// A second Inspector, so scoping tests can prove one Inspector cannot see another's Leads —
    /// which a single seeded Inspector could never demonstrate.
    /// </summary>
    public const string SecondInspectorEmail = "inspector2@renotrack.test";
    public const string SecondInspectorPassword = "Inspector2#Pass123";

    /// <summary>
    /// Deliberately assigned <b>no role at all</b>. Exists to prove the authorization path fails
    /// secure: an account that is authenticated but holds neither role must be refused, never
    /// silently treated as unrestricted.
    /// </summary>
    public const string NoRoleEmail = "norole@renotrack.test";
    public const string NoRolePassword = "NoRole#Pass123";

    public async Task InitializeAsync()
    {
        // The schema must exist before the host is first created: Program.cs seeds Identity roles
        // during startup, which fails against a database with no tables.
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        // Touching Services starts the host, which runs Program.cs's IdentityRoleSeeder — so the
        // Admin/Inspector roles exist before any user is assigned to one.
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedUserAsync(userManager, AdminEmail, AdminPassword, "Test Admin", "Admin", isActive: true);
        await SeedUserAsync(userManager, InspectorEmail, InspectorPassword, "Test Inspector", "Inspector", isActive: true);
        await SeedUserAsync(userManager, InactiveEmail, InactivePassword, "Inactive User", "Inspector", isActive: false);
        await SeedUserAsync(userManager, LockoutEmail, LockoutPassword, "Lockout User", "Inspector", isActive: true);
        await SeedUserAsync(userManager, SecondInspectorEmail, SecondInspectorPassword, "Second Inspector", "Inspector", isActive: true);
        await SeedUserAsync(userManager, NoRoleEmail, NoRolePassword, "No Role User", role: null, isActive: true);
    }

    /// <summary>Resolves a seeded user's id, so tests can assign Leads to a real Inspector.</summary>
    public async Task<int> GetUserIdAsync(string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Test user '{email}' was not seeded.");

        return user.Id;
    }

    /// <summary>
    /// Uses the real <see cref="UserManager{TUser}"/> — real password hashing, real role assignment,
    /// no shortcut around Identity (D58). A test that authenticated against a hand-built hash would
    /// prove nothing about whether login actually works.
    /// </summary>
    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string name,
        string? role,
        bool isActive)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = name,
            IsActive = isActive,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed test user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        if (role is not null)
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }

    public new async Task DisposeAsync()
    {
        await using (var context = CreateDbContext())
        {
            await context.Database.EnsureDeletedAsync();
        }

        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
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
