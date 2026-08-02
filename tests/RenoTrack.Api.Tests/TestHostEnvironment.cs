using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace RenoTrack.Api.Tests;

/// <summary>
/// Stands in for the <see cref="IHostEnvironment"/> the generic host registers for free, needed by
/// <c>DatabaseInitializer</c> (D63) when <c>AddInfrastructure()</c> is composed into an isolated
/// container rather than a real host — the same situation that already requires <c>AddLogging()</c>
/// there for <c>ILogger&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// Duplicated from <c>RenoTrack.Infrastructure.Tests</c> rather than shared: the two test projects
/// are deliberately independent assemblies, and introducing a shared test-support project to spare
/// four trivial properties would couple them for no real gain. Hand-written per CLAUDE.md §14's
/// no-mocking-framework rule.
///
/// Tests that boot the real application through <c>RenoTrackApiFactory</c> never need this — the
/// host supplies the genuine article.
/// </remarks>
internal sealed class TestHostEnvironment(string? environmentName = null) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName ?? Environments.Development;
    public string ApplicationName { get; set; } = "RenoTrack.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
