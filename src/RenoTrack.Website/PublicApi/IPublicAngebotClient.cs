namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The Website's one way to reach the API's customer-facing surface
/// (<c>GET /api/v1/public/angebote/{token}</c>, SRS FR-6.2, and
/// <c>POST .../decision</c>, SRS FR-6.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>An interface so a page can be tested without a server</b>, and so the boundary is a named
/// thing rather than an <c>HttpClient</c> call buried in a page model. It grows one method per real
/// use case, exactly as the repository interfaces do (<c>CLAUDE.md</c> §4): the read arrived with
/// Slice 2, and Slice 4 added the decision — two methods, because two use cases exist.
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

    /// <summary>
    /// Records the customer's answer against <paramref name="token"/> (SRS FR-6.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never throws for a failure the customer could cause or observe</b>, exactly as the read
    /// does not: an expired link, an already-answered link and an API outage are all values on
    /// <see cref="CustomerDecisionOutcome"/>.
    /// </para>
    /// <para>
    /// <b>Returns no document.</b> The caller redirects and re-reads, because what the API
    /// persisted is authoritative and what this call attempted is not — the case that makes the
    /// difference visible is two customers answering one link, where the loser must be shown the
    /// winner's decision.
    /// </para>
    /// </remarks>
    /// <param name="reason">
    /// FR-6.3's optional rejection reason (D98). Meaningful only with
    /// <see cref="CustomerDecisionChoice.Reject"/> — the API refuses one sent with an approval, so
    /// the confirmation page offers the field on one choice only rather than relying on that
    /// refusal. <b>The customer never sees it again:</b> it is staff-facing, and
    /// <c>CustomerAngebot</c> deliberately does not carry it back.
    /// </param>
    Task<CustomerDecisionOutcome> RecordDecisionAsync(
        string token,
        CustomerDecisionChoice choice,
        string? reason,
        CancellationToken cancellationToken);
}
