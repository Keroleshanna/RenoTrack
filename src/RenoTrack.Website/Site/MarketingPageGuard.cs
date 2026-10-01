using Microsoft.AspNetCore.Routing;

namespace RenoTrack.Website.Site;

/// <summary>
/// Fails startup if marketing-page metadata sits on any route whose URL carries a customer credential
/// (<b>D103</b>).
/// </summary>
/// <remarks>
/// <b>Keyed on a route parameter named <c>token</c></b>, the same rule <c>CustomerSecurityHeaders</c> keys
/// its strict headers on, so it keeps covering a future token route — an invoice link, a second decision
/// step — without anyone maintaining a list of paths. Route parameter names are case-insensitive, and so
/// is this check. Runs whether or not the marketing site is enabled; with it disabled, there is simply
/// nothing to find.
/// </remarks>
public static class MarketingPageGuard
{
    internal const string TokenRouteParameterName = "token";

    /// <exception cref="InvalidOperationException">An endpoint carries the metadata and a token parameter.</exception>
    public static void EnsureNoTokenRoutes(IEnumerable<Endpoint> endpoints)
    {
        var offending = endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<MarketingPageMetadata>() is not null)
            .Where(endpoint => endpoint.RoutePattern.Parameters.Any(parameter =>
                string.Equals(parameter.Name, TokenRouteParameterName, StringComparison.OrdinalIgnoreCase)))
            .Select(endpoint => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "(unnamed)")
            .ToList();

        if (offending.Count > 0)
        {
            throw new InvalidOperationException(
                "Marketing-page metadata is on a route whose URL carries a customer token: " +
                $"{string.Join(", ", offending.Select(route => $"'{route}'"))}. Token pages keep their own " +
                "security rules and must never receive marketing headers, redirects or layout (CLAUDE.md §24).");
        }
    }

    /// <summary>Every endpoint the application has mapped.</summary>
    public static IEnumerable<Endpoint> EndpointsOf(IEndpointRouteBuilder application) =>
        application.DataSources.SelectMany(source => source.Endpoints);
}
