using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// Builds a real UserManager/RoleManager pair via the same AddIdentityCore(...) chain
/// DependencyInjection.AddInfrastructure registers, pointed at the shared LocalDB test database
/// — a DI-built manager, not a hand-constructed one, so these tests exercise the actual
/// registration shape rather than a parallel approximation of it.
/// </summary>
internal static class IdentityTestServices
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackInfrastructureTests;Trusted_Connection=True;TrustServerCertificate=True";

    public static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RenoTrackDbContext>(options => options.UseSqlServer(ConnectionString));
        services.AddIdentityCore<RenoTrack.Infrastructure.Identity.ApplicationUser>()
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<RenoTrackDbContext>();

        return services.BuildServiceProvider();
    }
}
