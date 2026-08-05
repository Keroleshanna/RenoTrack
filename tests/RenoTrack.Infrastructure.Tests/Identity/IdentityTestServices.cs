using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RenoTrack.Infrastructure;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// Builds a real UserManager/RoleManager pair via the actual AddInfrastructure() registration
/// (Slice 14) — not a hand-rolled approximation of it — so these tests exercise the real DI
/// configuration and can never silently drift from it. The only test-specific override is the
/// connection string, supplied via configuration exactly like AddInfrastructure already expects
/// (ConnectionStrings:RenoTrackDb), pointed at the shared Infrastructure test database.
/// </summary>
internal static class IdentityTestServices
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackInfrastructureTests;Trusted_Connection=True;TrustServerCertificate=True";

    public static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RenoTrackDb"] = ConnectionString,

                // AddInfrastructure validates this eagerly as of Slice 8, so the container cannot
                // be built without it — even for tests that never touch file storage.
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "RenoTrackIdentityTests"),
                ["TokenLink:LifetimeDays"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // Same category as AddLogging: host-provided in production, absent in an isolated container,
        // and DatabaseInitializer's constructor needs it (D63).
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
