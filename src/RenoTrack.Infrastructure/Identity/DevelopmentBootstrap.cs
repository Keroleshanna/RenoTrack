using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Provisions the Development login accounts that make the API drivable by hand (D64). Nothing else.
///
/// <para><b>Why this is not part of <see cref="DatabaseInitializer"/>.</b> That component exists to
/// make exactly one statement — <em>this database is ready to serve</em> — and refuses to start the
/// application when it is not (D63). Whether a convenience account exists says nothing about
/// readiness, and folding it in would mean a component whose whole justification is a
/// least-privilege, read-only Production posture also owning the one operation that mints a
/// privileged credential. They are separate startup steps, in separate scopes, and this one checks
/// its own preconditions rather than relying on the other having run first.</para>
///
/// <para><b>This does not answer SRS OQ-1.</b> Whether Admins manage Inspector accounts from the
/// dashboard, or v1 provisions them directly in the database, is an open business question and is
/// untouched here. <b>No code path in this class is reachable in Production</b>, so production user
/// provisioning remains exactly as it was: nothing creates a user.</para>
///
/// <para>A dedicated DI service with <c>IServiceScopeFactory</c> injected by constructor, matching
/// <see cref="IdentityRoleSeeder"/> and <see cref="DatabaseInitializer"/> for the same reason (D55):
/// each account is an independent unit of work and gets its own scope, so a failed
/// <c>CreateAsync</c> cannot leave an entity tracked as Added on a shared <c>DbContext</c> and ride
/// along into the next account's <c>SaveChangesAsync</c>.</para>
/// </summary>
public sealed class DevelopmentBootstrap(
    IServiceScopeFactory scopeFactory,
    DevelopmentBootstrapOptions options,
    IHostEnvironment environment,
    ILogger<DevelopmentBootstrap> logger)
{
    /// <summary>
    /// Runs the configured provisioning. Throws — preventing the application from serving — when the
    /// feature is enabled somewhere it must not be, is enabled without a password, or is enabled
    /// against a database whose required roles are missing.
    /// </summary>
    /// <remarks>
    /// No <c>CancellationToken</c> parameter: nothing in <c>UserManager</c>/<c>RoleManager</c>'s
    /// surface accepts one anywhere in this call chain, so the parameter would be unused and
    /// misleading — the same reasoning as <see cref="IdentityRoleSeeder.SeedRolesAsync"/>.
    /// </remarks>
    public async Task RunAsync()
    {
        // Checked first, and quietly: an absent or false setting is the normal state of every
        // environment including Production, so this is the absence of an event rather than an event,
        // and it must not be an error, a warning, or an Information line every host logs on every
        // start. Debug rather than removed outright because this is the component's only silent path:
        // when someone sets Enabled=true and gets no accounts, the cause is nearly always that the
        // value never reached configuration (key typo, wrong appsettings file, user secrets not
        // loading), and this line is what separates "the flag never arrived" from "the flag arrived
        // and something else happened".
        if (!options.Enabled)
        {
            logger.LogDebug(
                "Development bootstrap is disabled ('{Key}' is not true); no accounts were provisioned.",
                DevelopmentBootstrapOptions.EnabledKey);
            return;
        }

        EnsureDevelopmentEnvironment();

        // Only after the environment guard — see DevelopmentBootstrapOptions.Validate's remarks for
        // why the order of these two failures matters.
        options.Validate();

        await EnsureRequiredRolesExistAsync();

        foreach (var account in options.Accounts)
        {
            await ProvisionAsync(account);
        }
    }

    /// <summary>
    /// A <b>positive allowlist</b> — deliberately stricter than
    /// <c>DatabaseInitializer</c>'s <c>!IsProduction()</c> refusal, and the asymmetry is the point.
    /// Migrating a Staging database is recoverable; silently minting a known-credential Admin on any
    /// reachable non-development host is not. This is CLAUDE.md §22's fail-secure rule applied
    /// literally: the privileged outcome is reached only by positively establishing the condition,
    /// never by failing to match an exclusion — so a new environment name, a typo, or an unset
    /// <c>ASPNETCORE_ENVIRONMENT</c> all land on refusal.
    ///
    /// <para>Enabled-but-not-permitted is a hard failure rather than a silent skip, for the same
    /// reason D63 refuses rather than warns: a deployment that skipped would run while its operator
    /// believed the opposite of what happened.</para>
    /// </summary>
    private void EnsureDevelopmentEnvironment()
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        throw new InvalidOperationException(
            $"Configuration '{DevelopmentBootstrapOptions.EnabledKey}' is true, but the current environment is " +
            $"'{environment.EnvironmentName}'. Development account provisioning is permitted only in the " +
            "Development environment — it is refused everywhere else, including Staging, and no code path " +
            "creates a user account in Production (ARCHITECTURE_DECISIONS.md D64; SRS OQ-1 remains open). " +
            $"Set '{DevelopmentBootstrapOptions.EnabledKey}' to false.");
    }

    /// <summary>
    /// This component's own precondition, checked here rather than assumed from
    /// <see cref="DatabaseInitializer"/> having run first. That independence is what makes the
    /// separation real: the ordering in <c>Program.cs</c> is a convenience, not something correctness
    /// rests on. <c>AddToRoleAsync</c> would fail on its own for a missing role, but with an Identity
    /// error description rather than a message naming the initialization step that seeds roles.
    /// </summary>
    private async Task EnsureRequiredRolesExistAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

        var missing = new List<string>();

        foreach (var role in options.Accounts.Select(a => a.Role).Distinct(StringComparer.Ordinal))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                missing.Add(role);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Cannot provision development accounts: required Identity role(s) missing from the database: " +
            $"{string.Join(", ", missing)}. Role reference data is seeded by database initialization " +
            $"('{DatabaseInitializationOptions.ModeKey}' = " +
            $"'{DatabaseInitializationMode.Migrate}'), which must run before this step.");
    }

    /// <summary>
    /// <b>Create-only.</b> An account that already exists is left completely untouched — no password
    /// reset, no role re-assignment, no reactivation, no rename.
    ///
    /// <para>Three reasons, in order of weight. A developer who deliberately deactivated the seeded
    /// Inspector to observe the rejected-login path must not have it silently reactivated by the next
    /// restart. A "repair" path is precisely the shape that turns dangerous if the environment guard
    /// ever fails, since the worst case of a guard bug becomes resetting an existing privileged
    /// account rather than adding one. And it keeps this contract identical to
    /// <see cref="IdentityRoleSeeder"/>'s, which is already create-only.</para>
    ///
    /// <para>The accepted cost, documented in the README: repairing a broken development account means
    /// deleting the row (or the database), not restarting the application.</para>
    /// </summary>
    private async Task ProvisionAsync(DevelopmentBootstrapAccount account)
    {
        // A fresh scope per account — not just a fresh UserManager — so a failed attempt for one
        // account can never leave residue affecting the next (D55).
        using var scope = scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var existing = await userManager.FindByEmailAsync(account.Email);

        if (existing is not null)
        {
            logger.LogInformation(
                "Development account {Email} already exists and was left untouched.", account.Email);

            await WarnIfExistingAccountLacksItsRoleAsync(userManager, existing, account);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = account.Email,
            Email = account.Email,
            Name = account.Name,
            IsActive = true,

            // No confirmation flow exists (AddInfrastructure deliberately registers no token
            // providers), so an unconfirmed account would simply be unusable.
            EmailConfirmed = true,
        };

        // Deliberately UserManager.CreateAsync(user, password): real hashing, real password policy.
        // A shortcut around Identity would provision an account whose ability to actually log in was
        // never established — the one thing this feature exists to guarantee.
        try
        {
            var result = await userManager.CreateAsync(user, account.Password!);

            if (!result.Succeeded)
            {
                // The check-then-create race, caught by Identity's own UserValidator before
                // SaveChangesAsync — benign if the account now exists (a concurrent starter won),
                // a real failure otherwise (typically a password-policy violation).
                if (await userManager.FindByEmailAsync(account.Email) is not null)
                {
                    logger.LogInformation(
                        "Development account {Email} was created concurrently; left untouched.", account.Email);
                    return;
                }

                // Identity's descriptions never echo the password itself, so this is safe to log.
                throw new InvalidOperationException(
                    $"Failed to provision development account '{account.Email}': " +
                    $"{string.Join(", ", result.Errors.Select(e => e.Description))}. " +
                    $"If this is a password-policy failure, change '{account.PasswordKey}'.");
            }
        }
        catch (DbUpdateException)
        {
            // The other manifestation of the same race, caught by AspNetUsers' unique index on
            // NormalizedUserName at SaveChangesAsync instead of by Identity's validator. await is
            // not allowed in a catch filter (CS7094), so the re-check happens in the body.
            if (await userManager.FindByEmailAsync(account.Email) is null)
            {
                throw;
            }

            logger.LogInformation(
                "Development account {Email} was created concurrently; left untouched.", account.Email);
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(user, account.Role);

        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Provisioned development account '{account.Email}' but failed to assign role " +
                $"'{account.Role}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}.");
        }

        logger.LogWarning(
            "Provisioned development account {Email} with role {Role}. This happens only in the " +
            "Development environment and only when '{Key}' is true.",
            account.Email,
            account.Role,
            DevelopmentBootstrapOptions.EnabledKey);
    }

    /// <summary>
    /// Reports — never repairs — an existing account that does not hold the role it was provisioned
    /// for. Reading role membership is not a mutation, so create-only is fully preserved: this
    /// component still never modifies an account it did not create.
    ///
    /// <para><b>The state this catches is reachable.</b> <c>CreateAsync</c> and
    /// <c>AddToRoleAsync</c> are two separate operations with no transaction spanning them, so a
    /// transient fault or a process kill between them leaves a role-less account. Startup fails that
    /// time — but on the next start the existence check above finds the account, skips it, and
    /// startup <em>succeeds</em>, leaving an account that can authenticate yet is refused by every
    /// <c>[Authorize(Roles = ...)]</c> endpoint. The security posture is fine (a role-less caller
    /// fails secure), but without this warning it presents as "login works and everything 403s" over
    /// a startup log that says nothing is wrong.</para>
    ///
    /// <para><b>Deliberately not called on the two concurrent-creation paths.</b> There, another
    /// instance has just created the account and may not have reached its own
    /// <c>AddToRoleAsync</c> yet — checking would report a race in progress as a defect. Do not
    /// "fix" that inconsistency; the check belongs only where the account predates this run.</para>
    /// </summary>
    private async Task WarnIfExistingAccountLacksItsRoleAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser existing,
        DevelopmentBootstrapAccount account)
    {
        if (await userManager.IsInRoleAsync(existing, account.Role))
        {
            return;
        }

        logger.LogWarning(
            "Development account {Email} exists but does not hold its expected role {Role}, so it will " +
            "be refused by every role-protected endpoint even though it can log in. This is not " +
            "repaired here — provisioning is create-only and never modifies an existing account. " +
            "Delete the account and restart to have it recreated.",
            account.Email,
            account.Role);
    }
}
