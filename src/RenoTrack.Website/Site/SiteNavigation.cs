namespace RenoTrack.Website.Site;

/// <summary>One entry of the marketing site's primary navigation.</summary>
public sealed record SiteNavigationItem(string Text, string Href);

/// <summary>
/// One step of a page's breadcrumb trail (<b>D105</b>). The current page has no <paramref name="Href"/>: it is
/// text marked <c>aria-current="page"</c>, never a link to itself.
/// </summary>
public sealed record BreadcrumbItem(string Text, string? Href = null);

/// <summary>
/// The primary navigation: <b>only pages that exist</b> (<b>D103</b>).
/// </summary>
/// <remarks>
/// Empty in Slice 2, because a link to a page that answers 404 would ship broken navigation into every
/// checkpoint. The homepage is the first entry (Slice 3, <b>D104</b>), the services overview the second (Slice 4,
/// <b>D105</b>). The slice that builds a page adds its entry here, and a test asserts every entry answers 200.
/// The narrow-screen <c>&lt;details&gt;</c> menu arrives when there are several real entries — two is not
/// several (S4-7).
/// </remarks>
public static class SiteNavigation
{
    public static IReadOnlyList<SiteNavigationItem> Primary { get; } =
    [
        new("Startseite", "/"),
        new("Leistungen", MarketingSite.ServicesPath),
    ];
}
