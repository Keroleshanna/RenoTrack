using System.Net;
using Microsoft.AspNetCore.Http;
using RenoTrack.Api.RateLimiting;

namespace RenoTrack.Api.Tests.RateLimiting;

/// <summary>
/// Partitioning is tested here, at unit level, and deliberately <b>not</b> through
/// <c>WebApplicationFactory</c>.
/// </summary>
/// <remarks>
/// <c>TestServer</c> supplies no <c>RemoteIpAddress</c> at all, so every request through the API
/// test host lands in the same partition. Making two API requests appear to come from different
/// clients would mean inserting a middleware that sets <c>Connection.RemoteIpAddress</c> from a
/// header — i.e. simulating the framework behaviour under test and proving nothing about it. A
/// real <see cref="HttpContext"/> with a real address is the faithful way to exercise the rule, so
/// that is what this does. What API-level coverage can and cannot prove is stated in
/// <c>PublicRateLimitEndpointTests</c>.
/// </remarks>
public sealed class PublicRateLimitPartitionTests
{
    private static HttpContext ContextFrom(string? ipAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ipAddress is null ? null : IPAddress.Parse(ipAddress);
        return context;
    }

    [Fact]
    public void Two_different_client_addresses_get_different_partitions() =>
        Assert.NotEqual(
            PublicRateLimitPartition.KeyFor(ContextFrom("203.0.113.7")),
            PublicRateLimitPartition.KeyFor(ContextFrom("203.0.113.8")));

    /// <summary>
    /// The other half: the same client must keep landing in the same bucket, or the limit would
    /// never actually bind.
    /// </summary>
    [Fact]
    public void The_same_client_address_gets_the_same_partition() =>
        Assert.Equal(
            PublicRateLimitPartition.KeyFor(ContextFrom("203.0.113.7")),
            PublicRateLimitPartition.KeyFor(ContextFrom("203.0.113.7")));

    [Fact]
    public void An_ipv6_client_is_partitioned_by_its_own_address() =>
        Assert.NotEqual(
            PublicRateLimitPartition.KeyFor(ContextFrom("2001:db8::1")),
            PublicRateLimitPartition.KeyFor(ContextFrom("2001:db8::2")));

    /// <summary>
    /// Unattributable requests share one allowance rather than each receiving a fresh one —
    /// otherwise "no remote address" would be the cheapest way to bypass the limiter entirely.
    /// </summary>
    [Fact]
    public void Requests_without_a_remote_address_share_one_partition() =>
        Assert.Equal(
            PublicRateLimitPartition.UnknownClientKey,
            PublicRateLimitPartition.KeyFor(ContextFrom(null)));

    /// <summary>
    /// X-Forwarded-For is never consulted. Trusting it without a known proxy trust boundary would
    /// let any caller mint a fresh partition per request and defeat the limiter completely — the
    /// precise reason ForwardedHeaders was left unconfigured rather than guessed (D65).
    /// </summary>
    [Fact]
    public void A_forwarded_for_header_does_not_influence_the_partition()
    {
        var spoofed = ContextFrom("203.0.113.7");
        spoofed.Request.Headers["X-Forwarded-For"] = "198.51.100.99";

        Assert.Equal(PublicRateLimitPartition.KeyFor(ContextFrom("203.0.113.7")), PublicRateLimitPartition.KeyFor(spoofed));
    }

    // ---- Options -----------------------------------------------------------

    /// <summary>The documented Phase 6 policy (D65), pinned so the number cannot drift silently.</summary>
    [Fact]
    public void The_default_policy_is_thirty_requests_per_minute()
    {
        var options = new PublicRateLimitOptions();

        Assert.Equal(30, options.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Window);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_permit_limit_fails_startup_naming_the_key(int permitLimit)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            new PublicRateLimitOptions { PermitLimit = permitLimit }.Validate);

        Assert.Contains(nameof(PublicRateLimitOptions.PermitLimit), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_window_fails_startup_naming_the_key(int windowSeconds)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            new PublicRateLimitOptions { WindowSeconds = windowSeconds }.Validate);

        Assert.Contains(nameof(PublicRateLimitOptions.WindowSeconds), exception.Message, StringComparison.Ordinal);
    }
}
