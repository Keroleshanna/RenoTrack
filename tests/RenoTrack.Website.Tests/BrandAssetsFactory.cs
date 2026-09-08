using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenoTrack.Website.Content;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Boots the real Website with a real <c>brand</c> directory, so the deployment-asset static-file
/// mount actually executes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mount is conditional on the directory existing</b>, so without this the middleware never
/// runs in the test suite at all and every claim about what it will and will not serve is a claim
/// about code no test touches. The content root is redirected to a temporary directory rather than
/// creating <c>brand/</c> inside the repository, so a crashed run cannot leave a stray directory in
/// the working tree.
/// </para>
/// <para>
/// Files are written before the host starts, because <c>Program.cs</c> decides whether to mount at
/// startup.
/// </para>
/// </remarks>
public sealed class BrandAssetsFactory : WebApplicationFactory<Program>
{
    /// <summary>A minimal but genuinely valid SVG — the shape a company logo would take.</summary>
    private const string LogoSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40"></svg>""";

    /// <summary>The name of a file placed *outside* the brand directory, beside it.</summary>
    internal const string SiblingFileName = "outside-the-mount.txt";

    internal string ContentRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        $"renotrack-brand-tests-{Guid.NewGuid():N}");

    internal string BrandRoot => Path.Combine(ContentRoot, CompanyIdentityOptions.BrandAssetsDirectoryName);

    public BrandAssetsFactory()
    {
        Directory.CreateDirectory(BrandRoot);

        File.WriteAllText(Path.Combine(BrandRoot, "logo.svg"), LogoSvg);

        // A known extension that is not an image. The mount is deliberately *not* image-only — it
        // serves any extension the default content-type provider recognises — and pinning that here
        // makes the breadth of the surface a stated decision rather than a surprise.
        File.WriteAllText(Path.Combine(BrandRoot, "notice.txt"), "brand asset");

        // An extension the content-type provider does not recognise, which ServeUnknownFileTypes
        // being false must refuse.
        File.WriteAllText(Path.Combine(BrandRoot, "notes.weirdext"), "must not be served");

        // Beside the brand directory, never inside it: the thing the mount must not reach.
        File.WriteAllText(Path.Combine(ContentRoot, SiblingFileName), "must not be served");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseContentRoot(ContentRoot);

        builder.UseSetting(
            $"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}",
            "https://api.example.test");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPublicAngebotClient>();
            services.AddSingleton<IPublicAngebotClient>(new UnusedPublicAngebotClient());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(ContentRoot))
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
    }

    /// <summary>These tests never reach the API; a call would be a defect, so it throws.</summary>
    private sealed class UnusedPublicAngebotClient : IPublicAngebotClient
    {
        public Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The brand-asset tests must not call the API.");

        public Task<CustomerDecisionOutcome> RecordDecisionAsync(
            string token,
            CustomerDecisionChoice choice,
            string? reason,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The brand-asset tests must not call the API.");
    }
}
