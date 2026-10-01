using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace RenoTrack.Website.Site;

/// <summary>
/// Re-executes a bodyless 404 into the marketing site's German not-found page (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered by <c>Program.cs</c> only when the marketing site is enabled.</b> A token-only deployment
/// keeps its bare 404s exactly as before.
/// </para>
/// <para>
/// <b>Deliberately narrower than <c>UseStatusCodePagesWithReExecute</c></b>, which would re-execute every
/// bodyless 4xx and 5xx and every method into a page that says "not found":
/// </para>
/// <list type="bullet">
/// <item><b>404 only.</b> A 405 or a bodyless 400 is not "page not found", and saying so would be untrue.</item>
/// <item><b>GET and HEAD only.</b> Re-executing a POST would run the not-found page's antiforgery check and
/// answer 400 instead of 404.</item>
/// <item><b>Never from a token route.</b> Those pages always render their own body, so this is defence in
/// depth, and it keeps a customer credential out of the re-execution feature entirely.</item>
/// </list>
/// <para>The not-found page never renders the original path or query, and nothing here logs.</para>
/// </remarks>
public static class SiteNotFound
{
    /// <summary>The not-found page's route.</summary>
    public const string PagePath = "/nicht-gefunden";

    /// <summary>Must be registered before <c>UseRouting</c>, so the re-executed request is routed afresh.</summary>
    public static IApplicationBuilder UseSiteNotFoundPage(this IApplicationBuilder app) =>
        app.UseStatusCodePages(new StatusCodePagesOptions { HandleAsync = ReExecuteAsync });

    private static async Task ReExecuteAsync(StatusCodeContext statusCodeContext)
    {
        var context = statusCodeContext.HttpContext;
        var request = context.Request;

        if (context.Response.StatusCode != StatusCodes.Status404NotFound
            || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || context.Request.RouteValues.ContainsKey(MarketingPageGuard.TokenRouteParameterName))
        {
            return;
        }

        var originalPath = request.Path;
        var originalQueryString = request.QueryString;

        context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPathBase = request.PathBase.Value!,
            OriginalPath = originalPath.Value!,
            OriginalQueryString = originalQueryString.HasValue ? originalQueryString.Value : null,
        });

        // The same reset the framework's own re-execution performs: without it, routing would keep the
        // endpoint it matched the first time.
        context.SetEndpoint(null);
        context.Features.Get<IRouteValuesFeature>()?.RouteValues?.Clear();

        request.Path = PagePath;
        request.QueryString = QueryString.Empty;

        try
        {
            await statusCodeContext.Next(context);
        }
        finally
        {
            request.QueryString = originalQueryString;
            request.Path = originalPath;
            context.Features.Set<IStatusCodeReExecuteFeature?>(null);
        }
    }
}
