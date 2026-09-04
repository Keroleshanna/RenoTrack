namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The Website's one way to reach the API's customer-facing surface
/// (<c>GET /api/v1/public/angebote/{token}</c>, SRS FR-6.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>An interface so a page can be tested without a server</b>, and so the boundary is a named
/// thing rather than an <c>HttpClient</c> call buried in a page model. It grows one method per real
/// use case, exactly as the repository interfaces do (<c>CLAUDE.md</c> §4) — recording the
/// customer's decision is Slice 4's method and is deliberately absent until then.
/// </para>
/// <para>
/// <b>Every call is server-side</b> (D97). The token never reaches a browser script, there is no
/// CORS surface, and the API's origin is never disclosed to the customer's browser.
/// </para>
/// </remarks>
public interface IPublicAngebotClient
{
    /// <summary>
    /// Fetches the Angebot behind <paramref name="token"/>.
    /// </summary>
    /// <remarks>
    /// <b>Never throws for a failure the customer could cause or observe.</b> An unknown token, an
    /// expired link, an API outage and a timeout are all outcomes on
    /// <see cref="CustomerAngebotResult"/>, not exceptions — so a page model has no reason to catch
    /// anything, and no path exists by which an API message becomes a customer-visible error.
    /// </remarks>
    Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken);
}
