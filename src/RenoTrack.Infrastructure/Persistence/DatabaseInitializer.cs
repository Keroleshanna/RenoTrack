using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// Decides what happens to the database at application startup (D63).
///
/// <para><b>The policy, in one sentence:</b> Production applies schema changes through an explicit
/// deployment step and the application only ever <em>verifies</em>; Development may opt into
/// migrating, and must say so in configuration.</para>
///
/// <para><b>Why not simply migrate at startup.</b> Automatic startup migration would require the
/// application's own runtime login to hold DDL permission permanently, turning any future
/// application-level compromise into a schema-destruction capability; it would apply schema
/// changes on every unplanned restart with no review, dry run, or backup window; and it races when
/// two instances start together — which Azure App Service does on every overlapped-restart deploy,
/// even at one configured instance. That last hazard is the same one D54/D55 refused to hand-wave
/// for role seeding, so relying on "we only run one instance" here would contradict a decision this
/// codebase has already made.</para>
///
/// <para><b>What replaced it.</b> The production deployment runs an EF migration bundle (or an
/// idempotent SQL script where a DBA reviews changes) before the application starts, and startup
/// then proves the database is actually ready. Verification is read-only, so the runtime login
/// needs no DDL rights at all.</para>
///
/// <para>A dedicated DI service with <c>IServiceScopeFactory</c> injected by constructor, matching
/// <see cref="IdentityRoleSeeder"/>'s shape for the same reason (D55): each step is an independent
/// unit of work and gets its own scope, so a failure in one cannot leave tracked residue affecting
/// the next.</para>
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    DatabaseInitializationOptions options,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>
    /// Runs the configured initialization. Throws — and so prevents the application from serving
    /// requests — if the database is unreachable, its migration history is incompatible with this
    /// build, or a required role is missing. Every one of those is a condition under which serving
    /// traffic would produce wrong behaviour rather than a clean failure.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Logged before anything happens, so the chosen behaviour is visible in startup logs rather
        // than inferred from what the process did to the database afterwards.
        logger.LogInformation(
            "Database initialization mode is {Mode} (environment: {Environment}).",
            options.Mode,
            environment.EnvironmentName);

        if (options.Mode == DatabaseInitializationMode.Migrate)
        {
            EnsureMigrateIsPermittedHere();

            await MigrateAsync(cancellationToken);
            await SeedRolesAsync();
        }

        await VerifyAsync(cancellationToken);

        logger.LogInformation("Database verification succeeded; the schema and required roles are present.");
    }

    /// <summary>
    /// Hard refusal, not a warning. A warning would be a rule nobody enforces: the whole point of
    /// D63 is that production schema mutation happens outside application startup, and a setting
    /// that merely logs disapproval before doing the dangerous thing anyway provides none of the
    /// least-privilege benefit that motivates the policy.
    /// </summary>
    private void EnsureMigrateIsPermittedHere()
    {
        if (!environment.IsProduction())
        {
            return;
        }

        throw new InvalidOperationException(
            $"Configuration '{DatabaseInitializationOptions.ModeKey}' is " +
            $"'{DatabaseInitializationMode.Migrate}', which is refused in the Production environment. " +
            "Production schema changes are applied by an explicit deployment step — an EF migration " +
            "bundle, or an idempotent SQL script in a DBA-controlled environment — never by application " +
            "startup (ARCHITECTURE_DECISIONS.md D63). Set it to " +
            $"'{DatabaseInitializationMode.Verify}' and apply migrations as part of deployment.");
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        logger.LogInformation("Applying database migrations.");
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds role <em>reference data</em> only. <b>No user account is created, in any environment.</b>
    /// Who may hold an account is an Admin-driven business action (PermissionMatrix.md §8) gated on
    /// SRS OQ-1, and is deliberately not something database initialization decides — a database
    /// having roles must never imply that a privileged user silently exists.
    /// </summary>
    private async Task SeedRolesAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IdentityRoleSeeder>();

        logger.LogInformation("Seeding Identity role reference data.");
        await seeder.SeedRolesAsync();
    }

    /// <summary>
    /// Read-only readiness check. Deliberately limited to <b>migration-history compatibility plus
    /// required roles</b> — not a schema diff. Comparing the migrations this build knows about
    /// against those recorded in <c>__EFMigrationsHistory</c> catches both directions of mismatch
    /// cheaply and unambiguously; a general schema-comparison engine would be a different, much
    /// larger thing and is explicitly not what this slice builds.
    /// </summary>
    private async Task VerifyAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        await VerifyMigrationHistoryAsync(dbContext, cancellationToken);
        await VerifyRequiredRolesAsync(scope.ServiceProvider);
    }

    private static async Task VerifyMigrationHistoryAsync(RenoTrackDbContext dbContext, CancellationToken cancellationToken)
    {
        // Checked first and separately, because the two failures are genuinely different and were
        // observed to be confusable: a database that does not exist surfaces as SQL error 4060,
        // which EF Core wraps in an "…likely due to a transient failure, consider enabling
        // EnableRetryOnFailure" message that points an operator at retry policies rather than at
        // the missing database. Naming it plainly here avoids that wild goose chase.
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Cannot connect to the RenoTrack database. Verify the 'RenoTrackDb' connection string, " +
                "that the database server is reachable, and that the database itself has been created — " +
                "a database that does not yet exist reports a login failure rather than a missing schema.");
        }

        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var known = dbContext.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);

        // Known but not applied: the database is behind this build.
        var pending = known.Except(applied).Order(StringComparer.Ordinal).ToList();

        // Applied but not known: the database is ahead of, or simply unknown to, this build —
        // typically a newer version was deployed and then rolled back to this one. Undetectable by
        // GetPendingMigrations alone, which only reports the other direction.
        var unknown = applied.Except(known).Order(StringComparer.Ordinal).ToList();

        if (pending.Count == 0 && unknown.Count == 0)
        {
            return;
        }

        var problems = new List<string>();

        if (pending.Count > 0)
        {
            problems.Add(
                $"the database is missing {pending.Count} migration(s) this application requires " +
                $"[{string.Join(", ", pending)}]");
        }

        if (unknown.Count > 0)
        {
            problems.Add(
                $"the database has {unknown.Count} applied migration(s) this application does not know about " +
                $"[{string.Join(", ", unknown)}], so its schema is newer than this build");
        }

        throw new InvalidOperationException(
            $"Database migration history is incompatible with this application: {string.Join("; and ", problems)}. " +
            "Apply the pending migrations as part of deployment, or deploy the application version that " +
            "matches the database (ARCHITECTURE_DECISIONS.md D63).");
    }

    private static async Task VerifyRequiredRolesAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

        var missing = new List<string>();

        foreach (var roleName in IdentityRoleSeeder.RoleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                missing.Add(roleName);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        // Startup must fail rather than continue: with a role missing, every [Authorize(Roles = ...)]
        // check silently denies, which presents as a fleet-wide permissions outage with no obvious
        // cause. A loud startup failure naming the role is strictly more useful.
        throw new InvalidOperationException(
            $"Required Identity role(s) missing from the database: {string.Join(", ", missing)}. " +
            "Role reference data is seeded by the explicit database initialization step, not by normal " +
            "application startup (ARCHITECTURE_DECISIONS.md D63).");
    }
}
