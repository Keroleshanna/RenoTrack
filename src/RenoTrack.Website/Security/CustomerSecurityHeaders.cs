namespace RenoTrack.Website.Security;

/// <summary>
/// The response headers every customer-facing page needs, and the stricter set a page whose URL
/// *is* a credential needs on top.
/// </summary>
/// <remarks>
/// <para>
/// <b>The token-route rules are keyed on a route parameter named <c>token</c></b>, not on a path
/// prefix — the same rule, for the same reason, that <c>RouteDiagnostics</c> uses on the API side.
/// It keeps applying when the credential is not the last segment (Slice 4's
/// <c>{token}/entscheidung</c>) and to a future invoice route, without anyone maintaining a list of
/// paths that must be remembered.
/// </para>
/// </remarks>
public static class CustomerSecurityHeaders
{
    /// <summary>
    /// The route parameter whose presence marks a URL as carrying a customer credential.
    /// </summary>
    internal const string TokenRouteParameterName = "token";

    /// <summary>
    /// Applies the headers. Must be registered **after** <c>UseRouting</c>, because the token rule
    /// reads the matched endpoint's route values — before routing there are none, and the strict
    /// headers would silently never be applied.
    /// </summary>
    public static IApplicationBuilder UseCustomerSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // Set on the way out rather than after next(), because headers cannot be added once the
            // response has begun.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                // A quote is a document, never a frame host or a sniffing target.
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";

                // No Referer on any navigation away from this site. On a token route this is not
                // hygiene but the difference between keeping and leaking the credential: without it
                // every outbound link hands the full token URL to whatever the customer clicks.
                headers["Referrer-Policy"] = "no-referrer";

                if (IsTokenRoute(context))
                {
                    // A priced quote behind a shared link must not be retained by an intermediary
                    // cache or restored from the browser's back-forward cache after the customer
                    // has walked away from a shared machine.
                    headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                    headers["Pragma"] = "no-cache";

                    // The URL is the credential, so it must not enter a search index. This is belt
                    // and braces — the link is unguessable and never published — but a crawler that
                    // reached one forwarded link would otherwise be free to keep it.
                    headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });

    private static bool IsTokenRoute(HttpContext context) =>
        context.Request.RouteValues.ContainsKey(TokenRouteParameterName);
}
