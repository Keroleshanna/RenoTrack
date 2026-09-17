using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>
/// What marketing pages need that is fixed at startup: the canonical origin (<b>D103</b>), the homepage's content
/// and services (<b>D104</b>), and the services overview and the service lookup (<b>D105</b>).
/// </summary>
/// <remarks>
/// <b>A startup snapshot, registered only when the marketing site is enabled.</b> The marketing layout reads
/// this, never <see cref="SiteOptions"/>, so a page renders from the same values the pipeline was composed
/// with. Only endpoints carrying <see cref="MarketingPageMetadata"/> render that layout, and those exist only
/// when this is registered.
/// </remarks>
public sealed class MarketingSite
{
    /// <summary>The services overview's path; a service page is <c>{ServicesPath}/{slug}</c>.</summary>
    public const string ServicesPath = "/leistungen";

    private readonly Dictionary<string, ServiceOptions> servicesBySlug;

    public MarketingSite(SiteOptions site)
    {
        CanonicalOrigin = site.CanonicalOrigin;
        Home = site.Home;
        ServicesPage = site.ServicesPage;
        Services = site.Services;

        // Ordinal and case-sensitive by construction (D105): a slug is looked up exactly as the company wrote it —
        // never case-folded, transliterated, fuzzy-matched or replaced by another service. Slugs are validated
        // unique under ordinal comparison, so this cannot throw.
        servicesBySlug = site.Services.ToDictionary(service => service.Slug!, StringComparer.Ordinal);
    }

    /// <summary>e.g. <c>https://www.example.test</c> — lower-cased host, no trailing slash.</summary>
    public string CanonicalOrigin { get; }

    /// <summary>The homepage's content, as validated at startup.</summary>
    public HomePageOptions Home { get; }

    /// <summary>The services overview's content, as validated at startup.</summary>
    public ServicesPageOptions ServicesPage { get; }

    /// <summary>The services, in the order the content pack lists them.</summary>
    public IReadOnlyList<ServiceOptions> Services { get; }

    /// <summary>The service with exactly this slug, or <c>null</c>. There is no fallback.</summary>
    public ServiceOptions? FindService(string? slug) =>
        slug is not null && servicesBySlug.TryGetValue(slug, out var service) ? service : null;

    /// <summary>The site-relative path of a service's page, e.g. <c>/leistungen/innenausbau</c>.</summary>
    public static string ServicePath(ServiceOptions service) => $"{ServicesPath}/{service.Slug}";

    /// <summary>The absolute canonical URL of a path on this site: lower case, no query.</summary>
    public string CanonicalUrlFor(PathString path) =>
        CanonicalOrigin + (path.HasValue ? path.Value!.ToLowerInvariant() : "/");
}
