using System.Net;
using Microsoft.AspNetCore.Http;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// D97: the Website forwards the real customer's address so the API's public rate limiter keeps
/// partitioning per customer instead of collapsing every customer into this Website's address.
/// </summary>
/// <remarks>
/// The security property under test is narrow and load-bearing: the value forwarded is the
/// <b>connection's</b> address and never a header the customer supplied. Relaying a customer-set
/// <c>X-Forwarded-For</c> would hand them control of their own rate-limit partition, which is
/// exactly the attack D65 refused to enable when it declined to read the header at all.
/// </remarks>
public sealed class ClientAddressForwardingHandlerTests
{
    /// <summary>
    /// Sends one request through the handler and returns the <c>X-Forwarded-For</c> values the API
    /// would actually have received — <c>null</c> when the header is absent entirely.
    /// </summary>
    /// <remarks>
    /// The values are read out here rather than the request being returned, so no test inspects a
    /// message after its <c>using</c> scope has ended.
    /// </remarks>
    private static async Task<IReadOnlyList<string>?> ForwardedValuesAsync(
        HttpContext? httpContext,
        Action<HttpRequestMessage>? prepareRequest = null)
    {
        var inner = StubHttpMessageHandler.Responding(HttpStatusCode.OK, "{}");
        var handler = new ClientAddressForwardingHandler(new StubHttpContextAccessor(httpContext))
        {
            InnerHandler = inner,
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/x");
        prepareRequest?.Invoke(request);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        var sent = Assert.Single(inner.Requests);
        return sent.Headers.TryGetValues(ClientAddressForwardingHandler.HeaderName, out var values)
            ? [.. values]
            : null;
    }

    private static HttpContext ContextFrom(string? remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
        return context;
    }

    [Fact]
    public async Task The_connections_address_is_forwarded()
    {
        var values = await ForwardedValuesAsync(ContextFrom("203.0.113.42"));

        Assert.Equal("203.0.113.42", Assert.Single(values!));
    }

    [Fact]
    public async Task An_ipv6_address_is_forwarded()
    {
        var values = await ForwardedValuesAsync(ContextFrom("2001:db8::1"));

        Assert.Equal("2001:db8::1", Assert.Single(values!));
    }

    /// <summary>
    /// The one that matters: a value already on the outgoing request is replaced, never appended to.
    /// An append would let a spoofed entry survive in the chain the API then reads.
    /// </summary>
    [Fact]
    public async Task A_preexisting_forwarded_for_header_is_replaced_not_appended()
    {
        var values = await ForwardedValuesAsync(
            ContextFrom("203.0.113.42"),
            request => request.Headers.TryAddWithoutValidation(
                ClientAddressForwardingHandler.HeaderName, "198.51.100.9"));

        Assert.NotNull(values);
        Assert.Equal("203.0.113.42", Assert.Single(values));
        Assert.DoesNotContain("198.51.100.9", string.Join(",", values), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is invented when there is no address to forward. The API then falls back to the
    /// connection address — this Website — which is the pre-D97 behaviour: less precise, never wrong.
    /// </summary>
    [Fact]
    public async Task No_header_is_sent_when_the_connection_has_no_address()
    {
        Assert.Null(await ForwardedValuesAsync(ContextFrom(null)));
    }

    [Fact]
    public async Task No_header_is_sent_outside_a_request()
    {
        Assert.Null(await ForwardedValuesAsync(httpContext: null));
    }

    /// <summary>
    /// And with no context, a header that was somehow already present is still removed rather than
    /// passed through — the handler never lets a value it did not set reach the API.
    /// </summary>
    [Fact]
    public async Task A_preexisting_header_is_stripped_even_when_there_is_nothing_to_replace_it_with()
    {
        var values = await ForwardedValuesAsync(
            httpContext: null,
            request => request.Headers.TryAddWithoutValidation(
                ClientAddressForwardingHandler.HeaderName, "198.51.100.9"));

        Assert.Null(values);
    }

    private sealed class StubHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => httpContext;
            set => httpContext = value;
        }
    }
}
