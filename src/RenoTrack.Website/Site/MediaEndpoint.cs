using Microsoft.Net.Http.Headers;

namespace RenoTrack.Website.Site;

/// <summary>
/// <c>GET/HEAD /medien/{datei}</c>: serves exactly the files <see cref="MediaCatalog"/> verified at startup, and
/// nothing else (<b>D106</b>, S5-2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mapped by <c>Program.cs</c> only when the marketing site is enabled.</b> A token-only deployment has no
/// <c>/medien/</c> at all.
/// </para>
/// <para>
/// <b>The requested name is a dictionary key, never a path.</b> An unknown name — any traversal attempt, any file
/// present on disk but not listed, any case variant — is a bodyless 404, which the site's not-found page renders
/// without echoing the name. The content type comes from the verified derivative, never from the request.
/// </para>
/// <para>
/// <b>Cached for a year as immutable (S5-12)</b>, which is safe only because every URL a page renders carries the
/// file's content hash. The <c>?v=</c> query itself is not checked here: a stale hash still gets the current file,
/// and the next page render links the new one. Nothing is logged.
/// </para>
/// </remarks>
public static class MediaEndpoint
{
    internal const string FileRouteParameterName = "datei";

    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints, MediaCatalog catalog) =>
        endpoints.MapMethods(
            $"{MediaCatalog.RequestPath}/{{{FileRouteParameterName}}}",
            [HttpMethods.Get, HttpMethods.Head],
            (string datei, HttpContext context) =>
            {
                var file = catalog.File(datei);
                if (file is null)
                {
                    return Results.NotFound();
                }

                context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Results.File(
                    file.AbsolutePath,
                    file.ContentType,
                    entityTag: new EntityTagHeaderValue($"\"{file.Version}\""));
            });
}
