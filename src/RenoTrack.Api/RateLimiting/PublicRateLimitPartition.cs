namespace RenoTrack.Api.RateLimiting;

/// <summary>
/// Which bucket a public request counts against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per client IP, because that is the only partition that answers the documented threat.</b>
/// Architecture.md §12 names "brute-forcing token guesses": partitioning per *token* would give
/// every guess its own fresh allowance and stop nothing, and a single global bucket would let one
/// abusive client consume the allowance of every genuine customer. Both were considered and
/// rejected (D65).
/// </para>
/// <para>
/// <b>The connection's <c>RemoteIpAddress</c> is used directly, and <c>X-Forwarded-For</c> is
/// deliberately never read.</b> Forwarded client-IP headers are attacker-controlled unless the
/// hosting trust boundary is known — which proxies are in front, how many hops, which networks are
/// trusted. None of that is knowable in Phase 6, and inventing it would be worse than leaving it
/// absent: a wrongly-trusted header lets any caller spoof a fresh partition per request and defeat
/// the limiter entirely. <b>Known consequence:</b> behind a reverse proxy without trusted
/// <c>ForwardedHeaders</c> configuration, clients collapse into the proxy's address and share one
/// bucket. That is a deployment prerequisite, tracked in <c>NEXT_STEPS.md</c> — not something to
/// paper over here.
/// </para>
/// </remarks>
internal static class PublicRateLimitPartition
{
    /// <summary>
    /// The bucket for a request whose remote address is unknown. Such requests share one allowance
    /// rather than each receiving a fresh one — an unattributable request must not be the cheapest
    /// way to bypass the limit.
    /// </summary>
    internal const string UnknownClientKey = "unknown";

    internal static string KeyFor(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClientKey;
}
