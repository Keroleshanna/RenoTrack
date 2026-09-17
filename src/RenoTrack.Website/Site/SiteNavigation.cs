namespace RenoTrack.Website.Site;

/// <summary>One entry of the marketing site's primary navigation.</summary>
public sealed record SiteNavigationItem(string Text, string Href);

/// <summary>
/// The primary navigation: <b>only pages that exist</b> (<b>D103</b>).
/// </summary>
/// <remarks>
/// Empty in Slice 2 — no marketing content page exists yet, and a link to a page that answers 404 would ship
/// broken navigation into every checkpoint. The slice that builds a page adds its entry here, and a test
/// asserts every entry answers 200.
/// </remarks>
public static class SiteNavigation
{
    public static IReadOnlyList<SiteNavigationItem> Primary { get; } = [];
}
