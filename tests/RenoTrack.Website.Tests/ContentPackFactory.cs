using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenoTrack.Website.Content;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// A content pack in a temporary directory, copied from a checked-in fictional fixture or written by
/// the test, and deleted afterwards (<b>D102</b>).
/// </summary>
/// <remarks>
/// Always a copy: a test that rewrites <c>site.json</c> must not be able to alter the fixture another
/// test reads. The directory is outside the application's content root, as a real pack is.
/// </remarks>
internal sealed class TemporaryContentPack : IDisposable
{
    internal const string Alpha = "Alpha";
    internal const string Beta = "Beta";

    private TemporaryContentPack()
    {
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"renotrack-content-pack-{Guid.NewGuid():N}");

    internal static string FixtureRoot(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentPacks", fixture);

    /// <summary>A copy of one of the fictional fixture packs.</summary>
    internal static TemporaryContentPack CopyOf(string fixture)
    {
        var pack = new TemporaryContentPack();
        var source = FixtureRoot(fixture);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(pack.Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return pack;
    }

    /// <summary>A pack whose files are exactly what the test writes.</summary>
    internal static TemporaryContentPack With(string? siteJson, string? legalJson = null)
    {
        var pack = new TemporaryContentPack();

        if (siteJson is not null)
        {
            pack.Write(ContentPackSectionPolicy.SiteFileName, siteJson);
        }

        if (legalJson is not null)
        {
            pack.Write(ContentPackSectionPolicy.LegalFileName, legalJson);
        }

        return pack;
    }

    internal void Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>
/// Boots the real Website against a content pack, with the API boundary stubbed so no test reaches a
/// socket. Production environment, as every customer-surface test here is.
/// </summary>
public sealed class ContentPackFactory : WebApplicationFactory<Program>
{
    private readonly string? packRoot;
    private readonly (string Key, string Value)[] settings;

    internal ContentPackFactory(string? packRoot, params (string Key, string Value)[] settings)
    {
        this.packRoot = packRoot;
        this.settings = settings;
    }

    /// <summary>What the stubbed API returns for any token.</summary>
    internal CustomerAngebotResult Result { get; set; } = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.UseSetting(
            $"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}",
            "https://api.example.test");

        if (packRoot is not null)
        {
            builder.UseSetting($"{ContentPackOptions.SectionName}:{nameof(ContentPackOptions.RootPath)}", packRoot);
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPublicAngebotClient>();
            services.AddSingleton<IPublicAngebotClient>(new StubPublicAngebotClient(this));
        });
    }

    private sealed class StubPublicAngebotClient(ContentPackFactory owner) : IPublicAngebotClient
    {
        public Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(owner.Result);

        public Task<CustomerDecisionOutcome> RecordDecisionAsync(
            string token,
            CustomerDecisionChoice choice,
            string? reason,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The content-pack tests do not record decisions.");
    }
}
