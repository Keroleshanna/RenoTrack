using System.Net.Http.Headers;

namespace RenoTrack.Website.PublicApi;

/// <summary>
/// Puts the real customer's address on every outgoing API call, so the API's public rate limiter
/// keeps partitioning per customer instead of collapsing every customer into this Website's own
/// address (D97, amending D65).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is necessary at all.</b> D65 chose to partition <c>/api/v1/public/*</c> by
/// <c>Connection.RemoteIpAddress</c> and never to read <c>X-Forwarded-For</c>, because trusting a
/// forwarded header with no known trust boundary lets any caller mint a fresh partition per request
/// and defeat the limiter entirely. That was right, and Architecture.md §12 records the consequence
/// as a deployment prerequisite. Server-side rendering makes it a live problem rather than a
/// prerequisite: without this handler every customer shares one 30-per-minute bucket, so one busy
/// afternoon — or one abusive visitor — throttles everybody else's quote.
/// </para>
/// <para>
/// <b>The value forwarded is the connection's own remote address, never a header the customer
/// sent.</b> Relaying an inbound <c>X-Forwarded-For</c> would hand the customer control of their own
/// rate-limit partition, which is precisely the attack D65 refused to enable. Any header already on
/// the outgoing request is removed before this one is set, so there is no append path and no way for
/// a spoofed value to survive.
/// </para>
/// <para>
/// <b>Composition with this Website's own proxy.</b> When the Website itself sits behind a reverse
/// proxy, <c>Connection.RemoteIpAddress</c> is the proxy's address unless the Website's own
/// <c>ForwardedHeaders</c> trust list names it — which is why <c>Program.cs</c> configures that with
/// the same allowlist shape. The two settings compose: the Website resolves the true client from
/// proxies it trusts, and forwards exactly that one value to the API, which trusts it only because
/// its own allowlist names this Website. Neither side trusts anything by default.
/// </para>
/// <para>
/// <b>A request with no HTTP context sends no header at all</b> — nothing is invented. The API then
/// falls back to the connection address, which is this Website: the pre-D97 behaviour, which is
/// degraded but never wrong.
/// </para>
/// </remarks>
public sealed class ClientAddressForwardingHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    internal const string HeaderName = "X-Forwarded-For";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(HeaderName);

        var clientAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (clientAddress is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderName, clientAddress.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
