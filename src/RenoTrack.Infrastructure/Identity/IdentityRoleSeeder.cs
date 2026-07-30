using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Seeds the two roles PermissionMatrix.md/CLAUDE.md §20 name as the only internal dashboard
/// roles — nothing else, no user accounts (those are an Admin-driven Application-layer action,
/// PermissionMatrix.md's "Create/deactivate Inspector accounts", not storage setup).
///
/// A dedicated DI service, not a static utility (D55) — matching the shape of every other
/// Infrastructure service in this project. IServiceScopeFactory is injected via the constructor
/// specifically so each role gets its own fresh scope (fresh RoleManager, fresh DbContext) —
/// not a service-locator shortcut, but the actual fix for a real bug found empirically: a failed
/// CreateAsync leaves that role's entity tracked as Added on its DbContext, and reusing the same
/// DbContext for the next role would let that stale entity ride along into the next role's
/// SaveChangesAsync call, cascading a failure for one role into a failure for an unrelated one.
/// Isolating each role in its own scope makes this structurally impossible rather than requiring
/// careful cleanup after the fact. The public SeedRolesAsync stays parameterless — how the
/// isolation is achieved is this class's own concern, not something callers need to supply.
///
/// Deterministic (same two fixed names every run) and idempotent — but the naive
/// check-then-create pattern (RoleExistsAsync, then CreateAsync if missing) is a real
/// check-then-act race: two application instances starting simultaneously could both observe a
/// role missing and both call CreateAsync. This race can surface two different ways, both
/// tolerated the same way (D54): re-check RoleExistsAsync, treat "already exists" as benign,
/// rethrow anything else unchanged.
///
/// 1. AspNetRoles' default unique index on NormalizedName (created by IdentityDbContext's own
///    base model, not something we configure) rejects the loser's INSERT — RoleStore surfaces
///    this as a DbUpdateException from SaveChangesAsync, not a graceful IdentityResult.
/// 2. Identity's own in-memory RoleValidator can also catch the same race one layer earlier,
///    re-checking uniqueness (via FindByNameAsync) before SaveChangesAsync is ever called — in
///    that case CreateAsync returns a graceful IdentityResult.Failed ("Role name 'X' is already
///    taken") instead of throwing at all.
/// </summary>
public sealed class IdentityRoleSeeder(IServiceScopeFactory scopeFactory)
{
    public static readonly string[] RoleNames = ["Admin", "Inspector"];

    // No CancellationToken parameter — RoleManager's API surface doesn't accept one anywhere
    // in this method's call chain, so a parameter here would be unused and misleading.
    public async Task SeedRolesAsync()
    {
        foreach (var roleName in RoleNames)
        {
            // A fresh scope per role — not just a fresh RoleManager — so a failed attempt for
            // one role can never leave residue that affects the next role's DbContext.
            using var scope = scopeFactory.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            try
            {
                var result = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
                if (!result.Succeeded && !await roleManager.RoleExistsAsync(roleName))
                {
                    // A concurrent instance did not win the race (the role still doesn't
                    // exist) — this is a real failure (e.g. an actual validation error), not
                    // the benign "already taken" manifestation of the race.
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
            catch (DbUpdateException)
            {
                // The other manifestation of the same race, caught at SaveChangesAsync instead
                // of by Identity's own validator — benign if the role now exists, a real
                // failure otherwise. await isn't allowed directly in a catch filter (CS7094),
                // so the re-check happens in the body instead.
                if (!await roleManager.RoleExistsAsync(roleName))
                    throw;
            }
        }
    }
}
