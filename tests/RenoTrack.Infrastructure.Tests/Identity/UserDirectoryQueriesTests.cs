using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Tests.Persistence;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// The staff directory, against real LocalDB.
/// </summary>
/// <remarks>
/// Covers the join across <c>AspNetUsers</c>/<c>AspNetUserRoles</c>/<c>AspNetRoles</c>, the role and
/// active filters, and the ordering. Its DI registration is pinned in <c>DependencyInjectionTests</c>
/// instead, where the container helper lives — necessarily explicitly, because the reflection-driven
/// safety net only discovers types in the Application assembly (D77).
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class UserDirectoryQueriesTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Creates a user in a role, returning the unique name used to find them again.</summary>
    private async Task<(int Id, string Name)> SeedAsync(string role, bool isActive = true)
    {
        var name = $"Person {Guid.NewGuid():N}"[..16];

        await using var context = fixture.CreateContext();

        var user = new ApplicationUser { Name = name, IsActive = isActive };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // The roles are reference data written by DatabaseInitializer at startup, not by the schema —
        // and this fixture creates the schema directly (EnsureCreated), so it has none. Seeded here
        // rather than assumed, which also keeps the test independent of startup ordering.
        var identityRole = context.Roles.SingleOrDefault(r => r.Name == role);
        if (identityRole is null)
        {
            identityRole = new Microsoft.AspNetCore.Identity.IdentityRole<int>
            {
                Name = role,
                NormalizedName = role.ToUpperInvariant(),
            };
            context.Roles.Add(identityRole);
            await context.SaveChangesAsync();
        }

        context.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int>
        {
            UserId = user.Id,
            RoleId = identityRole.Id,
        });
        await context.SaveChangesAsync();

        return (user.Id, name);
    }

    [Fact]
    public async Task Returns_a_users_name_and_role()
    {
        var (id, name) = await SeedAsync(IdentityRoleSeeder.InspectorRole);

        await using var context = fixture.CreateContext();
        var staff = await new UserDirectoryQueries(context).GetStaffAsync(null, true, default);

        var row = Assert.Single(staff, entry => entry.Id == id);
        Assert.Equal(name, row.Name);
        Assert.Equal(IdentityRoleSeeder.InspectorRole, row.Role);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Narrows_to_one_role()
    {
        var (inspectorId, _) = await SeedAsync(IdentityRoleSeeder.InspectorRole);
        var (adminId, _) = await SeedAsync(IdentityRoleSeeder.AdminRole);

        await using var context = fixture.CreateContext();
        var inspectors = await new UserDirectoryQueries(context)
            .GetStaffAsync(IdentityRoleSeeder.InspectorRole, true, default);

        Assert.Contains(inspectors, entry => entry.Id == inspectorId);
        Assert.DoesNotContain(inspectors, entry => entry.Id == adminId);
    }

    /// <summary>
    /// A deactivated account must not appear in an assignment dropdown — scheduling work to someone
    /// who can no longer sign in would fail later, at the point of the actual command.
    /// </summary>
    [Fact]
    public async Task Excludes_deactivated_accounts_unless_asked_for_them()
    {
        var (inactiveId, _) = await SeedAsync(IdentityRoleSeeder.InspectorRole, isActive: false);

        await using var context = fixture.CreateContext();
        var queries = new UserDirectoryQueries(context);

        var active = await queries.GetStaffAsync(IdentityRoleSeeder.InspectorRole, true, default);
        var everyone = await queries.GetStaffAsync(IdentityRoleSeeder.InspectorRole, false, default);

        Assert.DoesNotContain(active, entry => entry.Id == inactiveId);
        Assert.Contains(everyone, entry => entry.Id == inactiveId);
    }

    /// <summary>An unknown role name is an empty list, not an error — the honest answer.</summary>
    [Fact]
    public async Task Returns_nothing_for_a_role_that_does_not_exist()
    {
        await using var context = fixture.CreateContext();
        var staff = await new UserDirectoryQueries(context).GetStaffAsync("Gärtner", true, default);

        Assert.Empty(staff);
    }

    [Fact]
    public async Task Orders_by_name_so_the_list_reads_predictably()
    {
        await SeedAsync(IdentityRoleSeeder.InspectorRole);
        await SeedAsync(IdentityRoleSeeder.InspectorRole);

        await using var context = fixture.CreateContext();
        var staff = await new UserDirectoryQueries(context)
            .GetStaffAsync(IdentityRoleSeeder.InspectorRole, true, default);

        Assert.Equal(staff.Select(entry => entry.Name).OrderBy(name => name), staff.Select(entry => entry.Name));
    }

}
