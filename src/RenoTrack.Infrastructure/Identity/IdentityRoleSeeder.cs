using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Seeds the two roles PermissionMatrix.md/CLAUDE.md §20 name as the only internal dashboard
/// roles — nothing else, no user accounts (those are an Admin-driven Application-layer action,
/// PermissionMatrix.md's "Create/deactivate Inspector accounts", not storage setup).
///
/// Deterministic (same two fixed names every run) and idempotent — but the naive
/// check-then-create pattern (RoleExistsAsync, then CreateAsync if missing) is a real
/// check-then-act race: two application instances starting simultaneously could both observe a
/// role missing and both call CreateAsync. AspNetRoles' default unique index on NormalizedName
/// (created by IdentityDbContext's own base model, not something we configure) means the loser
/// fails with a DbUpdateException, not a graceful IdentityResult — RoleStore does not catch
/// SaveChanges failures into IdentityResult.Failed the way in-memory validation errors are.
/// This is tolerated explicitly, the same shape as D52's first-of-year INSERT race: catch the
/// failure, then re-check existence — if the role now exists, a concurrent instance won the
/// race and this is not a real failure; any other cause rethrows unchanged.
/// </summary>
public static class IdentityRoleSeeder
{
    public static readonly string[] RoleNames = ["Admin", "Inspector"];

    public static async Task SeedRolesAsync(RoleManager<IdentityRole<int>> roleManager)
    {
        foreach (var roleName in RoleNames)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            try
            {
                var result = await roleManager.CreateAsync(new IdentityRole<int>(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
            catch (DbUpdateException)
            {
                // A concurrent instance may have won the race and created this role first —
                // benign, not a real failure. Any other cause (the role genuinely still missing)
                // rethrows unchanged; await isn't allowed directly in a catch filter (CS7094),
                // so the re-check happens in the body instead.
                if (!await roleManager.RoleExistsAsync(roleName))
                    throw;
            }
        }
    }
}
