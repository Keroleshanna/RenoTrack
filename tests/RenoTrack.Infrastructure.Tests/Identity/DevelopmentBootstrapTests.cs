using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// The Development bootstrap policy of D64, proven against a real database and the real
/// <c>UserManager</c> — never a fake store, so "the seeded password actually logs in" is a fact
/// rather than an assumption.
/// </summary>
/// <remarks>
/// <para>
/// Uses its own throwaway database, following <see cref="Persistence.DatabaseInitializerTests"/>'s
/// pattern rather than joining the shared "Infrastructure Database" collection: those tests share an
/// <c>EnsureCreated</c> schema, and these need to create and destroy users and roles repeatedly.
/// </para>
/// <para>
/// Configuration keys are written out as literals rather than composed from constants, deliberately:
/// these exact strings are what the README tells a developer to type into
/// <c>dotnet user-secrets</c>, so a test that composed them could not catch a rename that silently
/// invalidated the documentation.
/// </para>
/// </remarks>
public sealed class DevelopmentBootstrapTests : IAsyncLifetime
{
    private const string DatabaseName = "RenoTrackDevelopmentBootstrapTests";

    private const string ConnectionString =
        $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";

    private const string AdminPasswordKey = "DevelopmentBootstrap:Admin:Password";
    private const string InspectorPasswordKey = "DevelopmentBootstrap:Inspector:Password";
    private const string AdminEmailKey = "DevelopmentBootstrap:Admin:Email";
    private const string InspectorEmailKey = "DevelopmentBootstrap:Inspector:Email";

    private const string AdminPassword = "Dev#Admin123";
    private const string InspectorPassword = "Dev#Inspector123";

    /// <summary>
    /// The documented default addresses, asserted as literals so a change to them is a deliberate,
    /// visible edit here as well as in the README.
    /// </summary>
    private const string AdminEmail = "dev-admin@renotrack.test";
    private const string InspectorEmail = "dev-inspector@renotrack.test";

    public async Task InitializeAsync() => await DropDatabaseAsync();

    public async Task DisposeAsync() => await DropDatabaseAsync();

    private static async Task DropDatabaseAsync()
    {
        await using var context = new RenoTrackDbContext(
            new DbContextOptionsBuilder<RenoTrackDbContext>().UseSqlServer(ConnectionString).Options);
        await context.Database.EnsureDeletedAsync();
    }

    // ---- Container / configuration helpers ---------------------------------

    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["ConnectionStrings:RenoTrackDb"] = ConnectionString,
        [$"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.RootPath)}"] =
            Path.Combine(Path.GetTempPath(), DatabaseName),
    };

    /// <summary>
    /// A passing configuration, with each password individually overridable — passing
    /// <see langword="null"/> omits that key entirely, which is how the "enabled but no password"
    /// failure is exercised.
    /// </summary>
    private static Dictionary<string, string?> EnabledSettings(
        string? adminPassword = AdminPassword,
        string? inspectorPassword = InspectorPassword)
    {
        var settings = BaseSettings();
        settings[DevelopmentBootstrapOptions.EnabledKey] = "true";

        if (adminPassword is not null)
        {
            settings[AdminPasswordKey] = adminPassword;
        }

        if (inspectorPassword is not null)
        {
            settings[InspectorPasswordKey] = inspectorPassword;
        }

        return settings;
    }

    /// <summary>
    /// Builds the real Infrastructure container exactly as <c>Program.cs</c> does, so the
    /// registrations under test are the production ones rather than a hand-assembled substitute.
    /// </summary>
    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        string environmentName,
        CapturingLoggerProvider? logProvider = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logProvider is not null)
            {
                builder.AddProvider(logProvider);
            }
        });
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    // environmentName defaults via null rather than a parameter default: Environments.Development is
    // a static readonly field, not a compile-time constant.
    private static async Task RunBootstrapAsync(
        Dictionary<string, string?> settings,
        string? environmentName = null,
        CapturingLoggerProvider? logProvider = null)
    {
        await using var provider = BuildProvider(
            settings, environmentName ?? Environments.Development, logProvider);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DevelopmentBootstrap>().RunAsync();
    }

    /// <summary>
    /// Brings the database to the state normal startup leaves it in — migrated, roles seeded, and
    /// <b>no users</b> — via the real <see cref="DatabaseInitializer"/>.
    /// </summary>
    private static async Task InitializeDatabaseAsync()
    {
        var settings = BaseSettings();
        settings[DatabaseInitializationOptions.ModeKey] = nameof(DatabaseInitializationMode.Migrate);

        await using var provider = BuildProvider(settings, Environments.Development);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    // ---- Assertion helpers -------------------------------------------------

    private static async Task<T> WithUserManagerAsync<T>(Func<UserManager<ApplicationUser>, Task<T>> read)
    {
        await using var provider = BuildProvider(BaseSettings(), Environments.Development);
        using var scope = provider.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private static Task<List<string>> UserEmailsAsync() =>
        WithUserManagerAsync(async users => await users.Users
            .Select(u => u.Email!)
            .OrderBy(e => e)
            .ToListAsync());

    private static Task<ApplicationUser?> FindUserAsync(string email) =>
        WithUserManagerAsync(users => users.FindByEmailAsync(email));

    private static Task<IList<string>> RolesOfAsync(string email) =>
        WithUserManagerAsync(async users =>
        {
            var user = await users.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"Expected account '{email}' to exist.");
            return await users.GetRolesAsync(user);
        });

    private static Task<bool> PasswordWorksAsync(string email, string password) =>
        WithUserManagerAsync(async users =>
        {
            var user = await users.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"Expected account '{email}' to exist.");
            return await users.CheckPasswordAsync(user, password);
        });

    // ---- Provisioning ------------------------------------------------------

    /// <summary>
    /// One Admin and one Inspector, each in exactly its own role. An Admin alone could not exercise
    /// any ownership-scoped path, which is why both exist.
    /// </summary>
    [Fact]
    public async Task Provisions_one_admin_and_one_inspector_each_in_its_own_role()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(EnabledSettings());

        Assert.Equal([AdminEmail, InspectorEmail], await UserEmailsAsync());
        Assert.Equal([IdentityRoleSeeder.AdminRole], await RolesOfAsync(AdminEmail));
        Assert.Equal([IdentityRoleSeeder.InspectorRole], await RolesOfAsync(InspectorEmail));
    }

    /// <summary>
    /// The whole point of the feature: the configured password must actually authenticate. Checked
    /// through the real <c>UserManager</c>'s hasher — an account provisioned around Identity would
    /// look correct in the database and still be unable to log in.
    /// </summary>
    [Fact]
    public async Task The_configured_password_authenticates_against_the_real_hasher()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(EnabledSettings());

        Assert.True(await PasswordWorksAsync(AdminEmail, AdminPassword));
        Assert.True(await PasswordWorksAsync(InspectorEmail, InspectorPassword));
        Assert.False(await PasswordWorksAsync(AdminEmail, InspectorPassword));
    }

    [Fact]
    public async Task Provisioned_accounts_are_active_and_usable_immediately()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(EnabledSettings());

        var admin = await FindUserAsync(AdminEmail);

        Assert.NotNull(admin);
        Assert.True(admin.IsActive);
        Assert.True(admin.EmailConfirmed);
    }

    [Fact]
    public async Task A_configured_email_overrides_the_default_address()
    {
        await InitializeDatabaseAsync();

        var settings = EnabledSettings();
        settings[AdminEmailKey] = "someone.else@renotrack.test";

        await RunBootstrapAsync(settings);

        Assert.Equal([InspectorEmail, "someone.else@renotrack.test"], await UserEmailsAsync());
    }

    // ---- Idempotency and create-only ---------------------------------------

    [Fact]
    public async Task Is_idempotent_across_repeated_runs()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(EnabledSettings());
        await RunBootstrapAsync(EnabledSettings());

        Assert.Equal([AdminEmail, InspectorEmail], await UserEmailsAsync());
        Assert.Equal([IdentityRoleSeeder.AdminRole], await RolesOfAsync(AdminEmail));
    }

    /// <summary>
    /// Create-only, stated as behaviour rather than intent: an account a developer has deliberately
    /// changed — a different password, deactivated to observe the rejected-login path — must survive
    /// the next restart untouched. A "repair" path would silently undo both.
    /// </summary>
    [Fact]
    public async Task An_existing_account_is_left_completely_untouched()
    {
        await InitializeDatabaseAsync();
        await RunBootstrapAsync(EnabledSettings());

        const string changedPassword = "Changed#Password456";

        await WithUserManagerAsync(async users =>
        {
            var admin = await users.FindByEmailAsync(AdminEmail)
                ?? throw new InvalidOperationException($"Expected account '{AdminEmail}' to exist.");

            await users.RemovePasswordAsync(admin);
            await users.AddPasswordAsync(admin, changedPassword);

            admin.IsActive = false;
            await users.UpdateAsync(admin);

            return true;
        });

        // A second run with the *original* configuration, which a repair path would use to reset.
        await RunBootstrapAsync(EnabledSettings());

        var reloaded = await FindUserAsync(AdminEmail);

        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsActive);
        Assert.True(await PasswordWorksAsync(AdminEmail, changedPassword));
        Assert.False(await PasswordWorksAsync(AdminEmail, AdminPassword));
    }

    /// <summary>
    /// The role-less-account case, which is reachable in practice: <c>CreateAsync</c> and
    /// <c>AddToRoleAsync</c> are two operations with no transaction across them, so a fault or a
    /// process kill between them leaves an account with no role — and every <em>subsequent</em>
    /// startup then skips it as "already exists" and succeeds, leaving an account that logs in but is
    /// refused everywhere. The warning is the only thing standing between that and a silent
    /// misdiagnosis, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task An_existing_account_missing_its_role_is_warned_about_but_still_not_repaired()
    {
        await InitializeDatabaseAsync();
        await RunBootstrapAsync(EnabledSettings());

        // Reproduce the post-crash state directly: the account exists, its role assignment does not.
        await WithUserManagerAsync(async users =>
        {
            var admin = await users.FindByEmailAsync(AdminEmail)
                ?? throw new InvalidOperationException($"Expected account '{AdminEmail}' to exist.");

            return await users.RemoveFromRoleAsync(admin, IdentityRoleSeeder.AdminRole);
        });

        var log = new CapturingLoggerProvider();

        // Must not throw: this is a report, not a new failure mode.
        await RunBootstrapAsync(EnabledSettings(), logProvider: log);

        // Scoped to this component's own category: the provider also sees EF Core's and Identity's
        // logging, and this assertion is about DevelopmentBootstrap's contract, not theirs.
        var warning = Assert.Single(
            log.EntriesFrom<DevelopmentBootstrap>(),
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(AdminEmail, StringComparison.Ordinal));

        Assert.Contains(IdentityRoleSeeder.AdminRole, warning.Message, StringComparison.Ordinal);

        // Reported, not repaired — create-only still holds for an account this run did not create.
        Assert.Empty(await RolesOfAsync(AdminEmail));

        // And the untouched account is genuinely untouched, not recreated.
        Assert.True(await PasswordWorksAsync(AdminEmail, AdminPassword));
    }

    /// <summary>
    /// The complement: an intact account produces no warning, so the check above cannot be passing
    /// for the trivial reason that it warns about everything.
    /// </summary>
    [Fact]
    public async Task An_existing_account_that_still_holds_its_role_produces_no_warning()
    {
        await InitializeDatabaseAsync();
        await RunBootstrapAsync(EnabledSettings());

        var log = new CapturingLoggerProvider();

        await RunBootstrapAsync(EnabledSettings(), logProvider: log);

        // "DevelopmentBootstrap emitted no warning" — deliberately not "nothing in the process
        // emitted a warning", which would tie this test to EF Core's and Identity's future logging.
        Assert.DoesNotContain(
            log.EntriesFrom<DevelopmentBootstrap>(),
            entry => entry.Level == LogLevel.Warning);
    }

    // ---- Environment policy ------------------------------------------------

    /// <summary>
    /// Enabled outside Development is a hard refusal, and it must fire before anything is written.
    ///
    /// <para>The guard is a <b>positive allowlist</b>, which is what the Staging and QA rows exist to
    /// pin: they are refused for the same reason Production is, rather than falling through because
    /// they merely are not Production. That is deliberately stricter than
    /// <see cref="DatabaseInitializer"/>'s own guard, so it is proven by a test rather than left to a
    /// comment.</para>
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public async Task Is_refused_outside_development_and_creates_nothing(string environmentName)
    {
        await InitializeDatabaseAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(EnabledSettings(), environmentName));

        Assert.Contains(DevelopmentBootstrapOptions.EnabledKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains(environmentName, exception.Message, StringComparison.Ordinal);

        Assert.Empty(await UserEmailsAsync());
    }

    [Fact]
    public async Task Is_permitted_in_development()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(EnabledSettings(), Environments.Development);

        Assert.NotEmpty(await UserEmailsAsync());
    }

    /// <summary>Disabled in a non-development environment is the normal case, and must be silent.</summary>
    [Fact]
    public async Task Disabled_in_production_is_a_silent_no_op()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(BaseSettings(), Environments.Production);

        Assert.Empty(await UserEmailsAsync());
    }

    // ---- Configuration -----------------------------------------------------

    /// <summary>An omitted key must mean disabled — the same fail-safe default as <c>Database:Mode</c>.</summary>
    [Fact]
    public async Task An_omitted_enabled_key_provisions_nothing()
    {
        await InitializeDatabaseAsync();

        await RunBootstrapAsync(BaseSettings());

        Assert.Empty(await UserEmailsAsync());
    }

    [Fact]
    public async Task Enabled_without_a_password_names_the_exact_key_and_creates_nothing()
    {
        await InitializeDatabaseAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(EnabledSettings(adminPassword: null)));

        Assert.Contains(AdminPasswordKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", exception.Message, StringComparison.Ordinal);

        Assert.Empty(await UserEmailsAsync());
    }

    /// <summary>
    /// A Production host enabled <em>and</em> missing a password must be told about the environment,
    /// not asked to supply a credential it must never supply. This pins the guard ordering.
    /// </summary>
    [Fact]
    public async Task In_production_the_environment_refusal_wins_over_the_missing_password()
    {
        await InitializeDatabaseAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(EnabledSettings(adminPassword: null), Environments.Production));

        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminPasswordKey, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A password Identity's own policy rejects must fail loudly and name the key to change — never
    /// be swallowed into an account that silently does not exist.
    /// </summary>
    [Fact]
    public async Task A_password_violating_identity_policy_fails_loudly_and_creates_nothing()
    {
        await InitializeDatabaseAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(EnabledSettings(adminPassword: "a")));

        Assert.Contains(AdminEmail, exception.Message, StringComparison.Ordinal);
        Assert.Contains(AdminPasswordKey, exception.Message, StringComparison.Ordinal);

        Assert.Empty(await UserEmailsAsync());
    }

    [Fact]
    public void An_unrecognised_enabled_value_fails_eagerly_and_names_the_key()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DevelopmentBootstrapOptions.EnabledKey] = "yes",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => DevelopmentBootstrapOptions.FromConfiguration(configuration));

        Assert.Contains(DevelopmentBootstrapOptions.EnabledKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("true", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A present-but-blank email falls back to the default address rather than failing. Unlike
    /// <c>Enabled</c> — where a typo must be loud, because it silently decides whether a privileged
    /// account is minted — a blank address has an obvious, harmless intended meaning and a default
    /// that is already documented, so treating it as an error would be pedantry with no safety gain.
    /// </summary>
    [Fact]
    public async Task A_blank_email_falls_back_to_the_default_address()
    {
        await InitializeDatabaseAsync();

        var settings = EnabledSettings();
        settings[AdminEmailKey] = "   ";

        await RunBootstrapAsync(settings);

        Assert.Equal([AdminEmail, InspectorEmail], await UserEmailsAsync());
    }

    /// <summary>
    /// Two accounts sharing an address must fail fast. Left unchecked this is the worst kind of
    /// misconfiguration: create-only provisioning would create the first account, find the address
    /// taken for the second, leave it untouched exactly as designed, and log a benign-sounding
    /// message — leaving one account holding only the Admin role with nothing indicating a problem.
    ///
    /// <para>The two addresses here differ only in case, which pins the case-insensitive comparison
    /// at the same time: Identity stores an upper-invariant <c>NormalizedEmail</c>, so these are one
    /// account to it, and an ordinal comparison would let this configuration through.</para>
    /// </summary>
    [Fact]
    public async Task Two_accounts_sharing_an_email_fail_fast_naming_both_keys_and_create_nothing()
    {
        await InitializeDatabaseAsync();

        var settings = EnabledSettings();
        settings[AdminEmailKey] = "shared@renotrack.test";
        settings[InspectorEmailKey] = "SHARED@renotrack.test";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(settings));

        Assert.Contains(AdminEmailKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains(InspectorEmailKey, exception.Message, StringComparison.Ordinal);

        // Not even the first account: the whole set is validated before anything is created, so the
        // half-provisioned state this check exists to prevent is never reachable.
        Assert.Empty(await UserEmailsAsync());
    }

    // ---- Preconditions -----------------------------------------------------

    /// <summary>
    /// This component checks its own precondition rather than assuming
    /// <see cref="DatabaseInitializer"/> ran first — which is what makes the two genuinely
    /// independent startup steps rather than one split in half.
    /// </summary>
    [Fact]
    public async Task Refuses_when_a_required_role_is_missing_and_creates_nothing()
    {
        await InitializeDatabaseAsync();

        await using (var context = new RenoTrackDbContext(
            new DbContextOptionsBuilder<RenoTrackDbContext>().UseSqlServer(ConnectionString).Options))
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [AspNetRoles] WHERE [Name] = {0}", IdentityRoleSeeder.InspectorRole);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunBootstrapAsync(EnabledSettings()));

        Assert.Contains(IdentityRoleSeeder.InspectorRole, exception.Message, StringComparison.Ordinal);

        // Not even the Admin account, whose own role is present: the precondition is checked for the
        // whole run before any account is created, so a half-provisioned state is impossible.
        Assert.Empty(await UserEmailsAsync());
    }

    // ---- Concurrency -------------------------------------------------------

    /// <summary>
    /// Concurrent application startup — the same hazard D54/D55 refused to hand-wave for roles, and
    /// identical in shape here because <c>AspNetUsers</c> carries a unique index on
    /// <c>NormalizedUserName</c>. The real assertion is that <c>Task.WhenAll</c> does not throw for
    /// any caller, and that the race produces one account, not two or a failure.
    /// </summary>
    /// <remarks>
    /// Per CLAUDE.md §14, a single green run of a concurrency test proves only that the race
    /// <em>can</em> succeed. This was re-run repeatedly before being trusted.
    /// </remarks>
    [Fact]
    public async Task Concurrent_runs_provision_exactly_one_of_each_account()
    {
        await InitializeDatabaseAsync();

        const int concurrentInstances = 10;

        await using var provider = BuildProvider(EnabledSettings(), Environments.Development);

        var tasks = Enumerable.Range(0, concurrentInstances).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DevelopmentBootstrap>().RunAsync();
        });

        await Task.WhenAll(tasks);

        Assert.Equal([AdminEmail, InspectorEmail], await UserEmailsAsync());
        Assert.Equal([IdentityRoleSeeder.AdminRole], await RolesOfAsync(AdminEmail));
        Assert.Equal([IdentityRoleSeeder.InspectorRole], await RolesOfAsync(InspectorEmail));
    }
}
