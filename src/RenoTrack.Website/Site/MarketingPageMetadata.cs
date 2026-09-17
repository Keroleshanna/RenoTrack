namespace RenoTrack.Website.Site;

/// <summary>
/// Endpoint metadata marking a page as part of the public marketing site (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not an attribute.</b> A page cannot declare itself a marketing page, and a token page
/// cannot acquire this by a stray <c>[MarketingPage]</c>. The only way it reaches an endpoint is
/// <see cref="MarketingPageConvention"/>, which <c>Program.cs</c> registers at startup and only when the
/// marketing site is enabled. When the site is disabled, no endpoint in the application carries it.
/// </para>
/// <para>
/// <b>Every request-time marketing behaviour asks exactly one question — does the matched endpoint carry
/// this?</b> The marketing security headers, the canonical-path redirect and the layout choice of a
/// shared page all decide from the endpoint, and none reads configuration per request. What a request
/// gets is therefore fixed by how the application was composed at startup, never by a value that could
/// change underneath it.
/// </para>
/// <para>
/// <b>Never on a token route.</b> <see cref="MarketingPageGuard"/> fails startup if an endpoint carrying
/// this has a route parameter named <c>token</c>: those pages keep their own, stricter rules
/// (<c>CLAUDE.md</c> §24).
/// </para>
/// </remarks>
public sealed class MarketingPageMetadata
{
    private MarketingPageMetadata()
    {
    }

    /// <summary>The single instance every marketing endpoint carries.</summary>
    public static MarketingPageMetadata Instance { get; } = new();
}

/// <summary>Reading <see cref="MarketingPageMetadata"/> off a request.</summary>
public static class MarketingPageEndpoint
{
    /// <summary>Whether the request's matched endpoint is a marketing page.</summary>
    public static bool IsMarketingPage(this HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<MarketingPageMetadata>() is not null;
}
