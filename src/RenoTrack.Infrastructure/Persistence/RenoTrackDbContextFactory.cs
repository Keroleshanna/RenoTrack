using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// Design-time only — used exclusively by `dotnet ef migrations add`/`database update` to
/// construct a RenoTrackDbContext without needing the app's real DI composition (that's Slice
/// 14, deliberately much later). The connection string here never needs to be reachable for
/// `migrations add` (it only builds the model), and is a LocalDB dev default consistent with
/// RenoTrack.Infrastructure.Tests' own fixture — not used by the running application at all.
/// </summary>
public sealed class RenoTrackDbContextFactory : IDesignTimeDbContextFactory<RenoTrackDbContext>
{
    public RenoTrackDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RenoTrackDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=RenoTrack;Trusted_Connection=True;TrustServerCertificate=True");

        return new RenoTrackDbContext(optionsBuilder.Options);
    }
}
