using System.Net;

namespace RenoTrack.Website.Site;

/// <summary>
/// One canonical address per page: the canonical host, and a lower-case path without a trailing slash
/// (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered by <c>Program.cs</c> only when the marketing site is enabled</b>, with the canonical origin
/// captured at startup. Nothing here reads configuration per request.
/// </para>
/// <para>
/// <b>Host.</b> Exactly one alias is redirected: the <c>www.</c> counterpart of the canonical host, derived
/// rather than configured. Every other host is left alone — redirecting "anything not canonical" would
/// bounce health checks and internal probes, and refusing unknown hosts is <c>AllowedHosts</c>' job. The
/// alias redirect applies to every route, token routes included, and copies the path unchanged: a token is
/// case-sensitive and must survive the hop byte for byte.
/// </para>
/// <para>
/// <b>Path.</b> Only endpoints carrying <see cref="MarketingPageMetadata"/> are canonicalised, which by
/// construction never includes a token route. A path beginning <c>//</c> or <c>/\</c> is never redirected:
/// echoed into a <c>Location</c>, a browser would read it as another host.
/// </para>
/// <para>Nothing is logged: on a token route the path is a credential (<c>CLAUDE.md</c> §24).</para>
/// </remarks>
public static class CanonicalRedirects
{
    /// <summary>Must be registered after <c>UseRouting</c>: the path rule reads the matched endpoint.</summary>
    public static IApplicationBuilder UseCanonicalRedirects(this IApplicationBuilder app, string canonicalOrigin)
    {
        var canonical = new Uri(canonicalOrigin, UriKind.Absolute);
        var alias = AliasHostFor(canonical.Host);

        return app.Use(async (context, next) =>
        {
            var request = context.Request;

            if (alias is not null && string.Equals(request.Host.Host, alias, StringComparison.OrdinalIgnoreCase))
            {
                Redirect(
                    context,
                    canonicalOrigin + request.PathBase.ToUriComponent() + request.Path.ToUriComponent() + request.QueryString.ToUriComponent(),
                    preserveMethod: !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method));
                return;
            }

            if (context.IsMarketingPage()
                && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && CanonicalPathFor(request.Path.Value) is { } canonicalPath)
            {
                Redirect(
                    context,
                    request.PathBase.ToUriComponent() + new PathString(canonicalPath).ToUriComponent() + request.QueryString.ToUriComponent(),
                    preserveMethod: false);
                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// The <c>www.</c> counterpart of <paramref name="canonicalHost"/>, or <c>null</c> when there is no
    /// meaningful one: an IP address, <c>localhost</c>, or a single-label host.
    /// </summary>
    internal static string? AliasHostFor(string canonicalHost)
    {
        var host = canonicalHost.ToLowerInvariant();

        if (IPAddress.TryParse(host.Trim('[', ']'), out _) || host == "localhost")
        {
            return null;
        }

        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            var bare = host["www.".Length..];
            return bare.Contains('.', StringComparison.Ordinal) ? bare : null;
        }

        return host.Contains('.', StringComparison.Ordinal) ? $"www.{host}" : null;
    }

    /// <summary>
    /// The canonical form of <paramref name="path"/>, or <c>null</c> when it already is canonical or must not
    /// be redirected.
    /// </summary>
    internal static string? CanonicalPathFor(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return null;
        }

        // Never produce a Location a browser would read as another host.
        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("/\\", StringComparison.Ordinal))
        {
            return null;
        }

        var canonical = path.TrimEnd('/').ToLowerInvariant();
        if (canonical.Length == 0)
        {
            canonical = "/";
        }

        return string.Equals(canonical, path, StringComparison.Ordinal) ? null : canonical;
    }

    private static void Redirect(HttpContext context, string location, bool preserveMethod)
    {
        context.Response.StatusCode = preserveMethod
            ? StatusCodes.Status308PermanentRedirect
            : StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = location;
    }
}
