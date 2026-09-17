using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace RenoTrack.Website.Site;

/// <summary>
/// The one place that turns pages into marketing pages (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered by <c>Program.cs</c> only when the marketing site is enabled</b>, decided once at startup.
/// When the site is disabled this never runs, so the legal pages keep exactly the behaviour they had
/// before Phase 13 and no endpoint carries <see cref="MarketingPageMetadata"/>.
/// </para>
/// <para>
/// A later slice that builds a marketing page adds its page path here, and nowhere else.
/// </para>
/// </remarks>
public static class MarketingPageConvention
{
    /// <summary>Page paths (Razor Pages page names, not URLs) that are marketing pages.</summary>
    public static IReadOnlyList<string> PagePaths { get; } =
    [
        "/Impressum",
        "/Datenschutz",
        "/NichtGefunden",
        "/Startseite",
    ];

    /// <summary>The page folder that holds the customer token pages; nothing under it may be a marketing page.</summary>
    internal const string TokenPagePrefix = "/Angebot";

    /// <exception cref="InvalidOperationException">A listed path names a customer token page.</exception>
    public static void Apply(PageConventionCollection conventions) => Apply(conventions, PagePaths);

    internal static void Apply(PageConventionCollection conventions, IEnumerable<string> pagePaths)
    {
        foreach (var pagePath in pagePaths)
        {
            // The first of two independent guards: a token page cannot be listed. The second,
            // MarketingPageGuard, inspects the built endpoints by route parameter, so it also catches a
            // token route that does not live under this prefix.
            if (pagePath.StartsWith(TokenPagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Page '{pagePath}' cannot be a marketing page: pages under '{TokenPagePrefix}' are customer " +
                    "token pages, which keep their own security rules (CLAUDE.md §24).");
            }

            conventions.AddPageApplicationModelConvention(
                pagePath,
                model => model.EndpointMetadata.Add(MarketingPageMetadata.Instance));
        }
    }
}
