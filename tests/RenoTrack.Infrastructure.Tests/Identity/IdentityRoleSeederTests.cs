using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// Proves IdentityRoleSeeder against real LocalDB, using the same DI-built RoleManager the real
/// app resolves (via IdentityTestServices). Deterministic and idempotent (run twice, still
/// exactly 2 roles), and — the specific concern raised in review — safe under concurrent
/// application startup, where two instances could both observe a role missing and race to create
/// it. AspNetRoles' unique index on NormalizedName (a framework default, not something this
/// project configures) makes the loser's INSERT fail; the seeder must tolerate that, not crash
/// startup or leave a duplicate row.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class IdentityRoleSeederTests
{
    [Fact]
    public async Task SeedRolesAsync_CreatesExactlyTheTwoDocumentedRoles()
    {
        await using var provider = IdentityTestServices.BuildProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            await RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync(roleManager);
        }

        await using var readScope = provider.CreateAsyncScope();
        var context = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var roleNames = await context.Roles.Select(r => r.Name).ToListAsync();
        Assert.Equal(["Admin", "Inspector"], roleNames.OrderBy(n => n));
    }

    [Fact]
    public async Task SeedRolesAsync_RunTwice_IsIdempotent()
    {
        await using var provider = IdentityTestServices.BuildProvider();

        await using (var firstScope = provider.CreateAsyncScope())
        {
            var roleManager = firstScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            await RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync(roleManager);
        }

        await using (var secondScope = provider.CreateAsyncScope())
        {
            var roleManager = secondScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            await RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync(roleManager);
        }

        await using var readScope = provider.CreateAsyncScope();
        var context = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var roleCount = await context.Roles.CountAsync();
        Assert.Equal(2, roleCount);
    }

    [Fact]
    public async Task SeedRolesAsync_CalledConcurrentlyByMultipleInstances_NeverProducesDuplicatesOrThrows()
    {
        // Simulates concurrent application startup: N "instances" each get their own scope
        // (matching a real Scoped-per-request/per-host lifetime) and race to seed the same
        // roles. The real assertion is that Task.WhenAll below does not throw for any caller.
        await using var provider = IdentityTestServices.BuildProvider();
        const int concurrentInstances = 10;

        var tasks = Enumerable.Range(0, concurrentInstances).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            await RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync(roleManager);
        });

        await Task.WhenAll(tasks);

        await using var readScope = provider.CreateAsyncScope();
        var context = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var roleNames = await context.Roles.Select(r => r.Name).ToListAsync();
        Assert.Equal(2, roleNames.Count);
        Assert.Equal(["Admin", "Inspector"], roleNames.OrderBy(n => n));
    }
}
