using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace RenoTrack.Infrastructure.Tests;

/// <summary>
/// Stands in for the <see cref="IHostEnvironment"/> the generic host registers for free, which an
/// isolated <c>ServiceCollection</c> does not — exactly the same situation as <c>AddLogging()</c>
/// being required in these containers for <c>ILogger&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// Hand-written rather than a mocking framework (CLAUDE.md §14), and deliberately not
/// <c>Microsoft.Extensions.Hosting.Internal.HostingEnvironment</c>: that type lives in the full
/// Hosting package rather than Hosting.Abstractions, and depending on a type whose namespace says
/// "Internal" to test a public contract is a poor trade for four trivial properties.
///
/// <see cref="EnvironmentName"/> is the only meaningful member — it is the single input
/// <c>DatabaseInitializer</c>'s Production guard reads. Defaults to Development so a container that
/// does not care about the environment never accidentally opts into Production semantics.
/// </remarks>
internal sealed class TestHostEnvironment(string? environmentName = null) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName ?? Environments.Development;
    public string ApplicationName { get; set; } = "RenoTrack.Infrastructure.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
