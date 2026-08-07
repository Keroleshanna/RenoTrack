namespace RenoTrack.Api.ErrorHandling;

/// <summary>
/// How a request may be described in diagnostic output — logs and RFC 7807 <c>instance</c> — when
/// part of its URL is a credential.
/// </summary>
/// <remarks>
/// <para>
/// <b>The property this exists to hold: a public token credential must never appear in a
/// diagnostic or error surface.</b> Phase 6 introduced the first routes whose path segment is
/// itself the secret, so the usual "log the path, echo the path" habit silently publishes a working
/// customer credential. A URL is not a safe identifier once a segment of it is a password.
/// </para>
/// <para>
/// Echoing it back to the caller who sent it looks harmless — they already hold it — but error
/// responses are captured far more widely than requests: reverse proxies, frontend telemetry,
/// support tooling and browser diagnostics all retain them. One simple, always-true rule is worth
/// more than case-by-case reasoning about which of those is in play.
/// </para>
/// <para>
/// <b>Why the template is captured up front instead of read on demand.</b> ASP.NET's exception
/// middleware calls <c>ClearHttpContext</c> before invoking any <c>IExceptionHandler</c>, which
/// nulls the endpoint and the route values — so by the time an exception is mapped or
/// ProblemDetails is customised, <c>HttpContext.GetEndpoint()</c> returns null and the route
/// template is simply gone. Reading it late therefore fell back to the raw path and leaked the
/// token anyway. This was verified empirically, by probe, after the first attempt silently failed
/// to redact anything. <c>HttpContext.Items</c> survives that clearing, so
/// <see cref="Capture"/> runs while routing metadata still exists and stashes what the later
/// surfaces need.
/// </para>
/// <para>
/// Keyed on a route <em>parameter named <c>token</c></em> rather than on a URL prefix or a segment
/// position, so it keeps holding for Slice 4's <c>{token}/decision</c> route — where the credential
/// is not the last segment — and for Phase 8's invoice links, without anyone remembering to extend
/// a list. A route without that parameter is untouched: non-public endpoints keep the exact
/// <c>instance</c> they had before.
/// </para>
/// </remarks>
internal static class RouteDiagnostics
{
    /// <summary>The route parameter this codebase uses for customer token credentials.</summary>
    private const string CredentialParameterName = "token";

    private const string TemplateKey = "RenoTrack.RouteTemplate";
    private const string CredentialKey = "RenoTrack.RouteCarriesCredential";

    /// <summary>
    /// Records the matched route template and whether it carries a credential. Must run after
    /// routing has selected an endpoint and before anything can throw — see the remarks above for
    /// why reading this later is not an option.
    /// </summary>
    internal static void Capture(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint() is not RouteEndpoint endpoint)
        {
            return;
        }

        httpContext.Items[TemplateKey] = endpoint.RoutePattern.RawText;
        httpContext.Items[CredentialKey] = endpoint.RoutePattern.Parameters
            .Any(parameter => string.Equals(parameter.Name, CredentialParameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The matched route template (<c>api/v1/public/angebote/{token}</c>), or null when no endpoint
    /// matched — which cannot be a token route, since an unmatched request never reaches a handler.
    /// </summary>
    internal static string? Template(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(TemplateKey, out var template) ? template as string : null;

    private static bool CarriesCredentialInPath(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(CredentialKey, out var carries) && carries is true;

    /// <summary>
    /// What may safely be published as ProblemDetails <c>instance</c>: the route template for a
    /// credential-bearing route, the real path for everything else. The template still answers the
    /// question <c>instance</c> exists to answer — which endpoint produced this — without the
    /// secret that identifying the exact resource would require.
    /// </summary>
    internal static string SafeInstance(HttpContext httpContext) =>
        CarriesCredentialInPath(httpContext) && Template(httpContext) is { } template
            ? $"/{template}"
            : httpContext.Request.Path.ToString();
}
