using RenoTrack.Website.Site;

namespace RenoTrack.Website.Security;

/// <summary>
/// The Content-Security-Policy and Permissions-Policy of marketing pages (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided from the matched endpoint alone.</b> A response gets these headers exactly when its endpoint
/// carries <see cref="MarketingPageMetadata"/>, which only the startup convention can add. No configuration
/// is read per request, so the headers cannot drift from how the application was composed.
/// </para>
/// <para>
/// <b>Token pages are untouched.</b> The metadata can never sit on a token route
/// (<see cref="MarketingPageGuard"/>), so their headers stay exactly as <see cref="CustomerSecurityHeaders"/>
/// sets them. The site-wide baseline — <c>nosniff</c>, <c>X-Frame-Options</c>, <c>Referrer-Policy</c> — still
/// comes from there, for every page.
/// </para>
/// <para>
/// <b><c>script-src 'none'</c> and <c>style-src 'self'</c> are commitments, not defaults</b>: marketing pages
/// run no JavaScript (Phase 13 Q15) and carry no inline style. <c>connect-src 'none'</c> closes the one
/// fetch channel <c>default-src 'self'</c> would otherwise leave open.
/// </para>
/// </remarks>
public static class MarketingSecurityHeaders
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; " +
        "connect-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'";

    internal const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), browsing-topics=()";

    /// <summary>Must be registered after <c>UseRouting</c>: it reads the matched endpoint.</summary>
    public static IApplicationBuilder UseMarketingSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                // Read when the response starts rather than when the request arrives: a 404 re-executed into
                // the marketing not-found page has that page's endpoint by then.
                if (context.IsMarketingPage())
                {
                    context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
                    context.Response.Headers["Permissions-Policy"] = PermissionsPolicy;
                }

                return Task.CompletedTask;
            });

            await next(context);
        });
}
