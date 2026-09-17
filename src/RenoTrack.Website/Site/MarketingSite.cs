using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>
/// What marketing pages need that is fixed at startup: the canonical origin (<b>D103</b>).
/// </summary>
/// <remarks>
/// <b>A startup snapshot, registered only when the marketing site is enabled.</b> The marketing layout reads
/// this, never <see cref="SiteOptions"/>, so a page renders from the same values the pipeline was composed
/// with. Only endpoints carrying <see cref="MarketingPageMetadata"/> render that layout, and those exist only
/// when this is registered.
/// </remarks>
public sealed class MarketingSite(SiteOptions site)
{
    /// <summary>e.g. <c>https://www.example.test</c> — lower-cased host, no trailing slash.</summary>
    public string CanonicalOrigin { get; } = site.CanonicalOrigin;

    /// <summary>The absolute canonical URL of a path on this site: lower case, no query.</summary>
    public string CanonicalUrlFor(PathString path) =>
        CanonicalOrigin + (path.HasValue ? path.Value!.ToLowerInvariant() : "/");
}
