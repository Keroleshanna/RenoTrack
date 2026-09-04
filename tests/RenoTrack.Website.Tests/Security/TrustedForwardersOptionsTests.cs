using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using RenoTrack.Website.Security;

namespace RenoTrack.Website.Tests.Security;

/// <summary>
/// D97's trust boundary. The property that matters is what happens when nothing is configured:
/// trusting nothing, so a deployment that has expressed no opinion cannot be talked into believing
/// a header any caller can set.
/// </summary>
public sealed class TrustedForwardersOptionsTests
{
    [Fact]
    public void Nothing_configured_means_nothing_is_trusted()
    {
        var options = new TrustedForwardersOptions();

        Assert.False(options.IsConfigured);
    }

    /// <summary>
    /// ASP.NET Core pre-trusts loopback in its own defaults. Cleared here, so the only trusted
    /// forwarders are the ones configuration names — otherwise anything sharing the host could
    /// assert a client address.
    /// </summary>
    [Fact]
    public void The_frameworks_pre_trusted_loopback_defaults_are_cleared()
    {
        var built = new TrustedForwardersOptions().Build();

        Assert.Empty(built.KnownProxies);
        Assert.Empty(built.KnownIPNetworks);
    }

    [Fact]
    public void A_configured_proxy_is_trusted()
    {
        var built = new TrustedForwardersOptions { KnownProxies = ["10.0.0.7"] }.Build();

        Assert.Equal(IPAddress.Parse("10.0.0.7"), Assert.Single(built.KnownProxies));
        Assert.True(new TrustedForwardersOptions { KnownProxies = ["10.0.0.7"] }.IsConfigured);
    }

    [Fact]
    public void A_configured_network_is_trusted()
    {
        var built = new TrustedForwardersOptions { KnownNetworks = ["10.0.0.0/8"] }.Build();

        Assert.Equal(System.Net.IPNetwork.Parse("10.0.0.0/8"), Assert.Single(built.KnownIPNetworks));
        Assert.True(new TrustedForwardersOptions { KnownNetworks = ["10.0.0.0/8"] }.IsConfigured);
    }

    /// <summary>
    /// Only the two headers the limiter and HTTPS redirection depend on, and one hop. A larger
    /// forward limit would let a forwarder trusted for one hop assert an entire chain.
    /// </summary>
    [Fact]
    public void Only_for_and_proto_are_honoured_and_only_one_hop()
    {
        var built = new TrustedForwardersOptions().Build();

        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            built.ForwardedHeaders);
        Assert.Equal(1, built.ForwardLimit);
    }

    /// <summary>
    /// A typo must not silently shrink the trust list. A limiter that looks configured and is not
    /// is worse than one that is openly unconfigured, so a malformed entry fails startup naming
    /// the key.
    /// </summary>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/8")]
    [InlineData("")]
    public void A_malformed_proxy_fails_startup_naming_the_key(string proxy)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TrustedForwardersOptions { KnownProxies = [proxy] }.Build());

        Assert.Contains(nameof(TrustedForwardersOptions.KnownProxies), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/notanumber")]
    [InlineData("garbage/8")]
    [InlineData("10.0.0.0/8/9")]
    [InlineData("")]
    public void A_malformed_network_fails_startup_naming_the_key(string network)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TrustedForwardersOptions { KnownNetworks = [network] }.Build());

        Assert.Contains(nameof(TrustedForwardersOptions.KnownNetworks), exception.Message, StringComparison.Ordinal);
    }

    // ---- Canonicality (D97) ------------------------------------------------

    /// <summary>
    /// A non-canonical network is refused at startup rather than silently widened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This guard is ours, because the framework's is documented but not implemented.</b>
    /// <c>System.Net.IPNetwork</c>'s type remark states that "the constructor and the parsing
    /// methods will throw in case there are non-zero bits after the prefix", and its constructor
    /// declares an <c>ArgumentException</c> saying the same. Neither happens: the shipped
    /// implementation calls <c>ClearNonZeroBitsAfterNetworkPrefix</c> and normalises silently.
    /// Established by reading dotnet/runtime at tag <c>v10.0.11</c>, the version CI installs — an
    /// earlier revision of this test asserted a rejection that never occurred, precisely because it
    /// trusted that documentation.
    /// </para>
    /// <para>
    /// The consequence without this guard: <c>10.0.0.1/8</c> would trust all 16,777,216 addresses in
    /// <c>10.0.0.0/8</c>. D97 requires a trust list to mean exactly what it says, and a silent
    /// widening is the same failure as a silent shrinking with a worse blast radius.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("10.0.0.1/8", "10.0.0.0/8")]
    [InlineData("192.168.1.5/24", "192.168.1.0/24")]
    [InlineData("172.16.5.1/12", "172.16.0.0/12")]
    public void A_non_canonical_network_is_refused_rather_than_silently_widened(
        string configured, string wouldHaveBecome)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TrustedForwardersOptions { KnownNetworks = [configured] }.Build());

        Assert.Contains(nameof(TrustedForwardersOptions.KnownNetworks), exception.Message, StringComparison.Ordinal);
        Assert.Contains(configured, exception.Message, StringComparison.Ordinal);

        // The message names what would have been trusted, so an operator can see the size of the
        // mistake rather than only that it was rejected.
        Assert.Contains(wouldHaveBecome, exception.Message, StringComparison.Ordinal);

        // And points at the right home for a single host.
        Assert.Contains(nameof(TrustedForwardersOptions.KnownProxies), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canonical form of the same network is accepted — the guard rejects non-canonical input,
    /// not the network itself.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.168.1.0/24")]
    [InlineData("172.16.0.0/12")]
    [InlineData("203.0.113.7/32")]
    [InlineData("2001:db8::/32")]
    public void A_canonical_network_is_accepted(string network)
    {
        var built = new TrustedForwardersOptions { KnownNetworks = [network] }.Build();

        Assert.Equal(System.Net.IPNetwork.Parse(network), Assert.Single(built.KnownIPNetworks));
    }

    /// <summary>
    /// What the accepted network actually trusts, asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// <c>ForwardedHeadersMiddleware.CheckKnownAddress</c> iterates <c>KnownIPNetworks</c> calling
    /// <c>network.Contains(address)</c>, so this is the real decision the trust boundary makes. It is
    /// pinned here because the containment semantics were the open security question behind D97's
    /// canonicality guard, and "we read the source once" is weaker than a test that fails if it
    /// changes.
    /// </remarks>
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.0.0.50", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("10.0.0.0", true)]
    [InlineData("11.0.0.1", false)]
    [InlineData("9.255.255.255", false)]
    [InlineData("192.168.0.1", false)]
    public void A_canonical_slash_eight_trusts_exactly_that_range(string candidate, bool expected)
    {
        var built = new TrustedForwardersOptions { KnownNetworks = ["10.0.0.0/8"] }.Build();
        var network = Assert.Single(built.KnownIPNetworks);

        Assert.Equal(expected, network.Contains(IPAddress.Parse(candidate)));
    }

    /// <summary>
    /// Nothing outside the configured entries is trusted, and in particular the framework's own
    /// pre-trusted loopback defaults do not survive <see cref="TrustedForwardersOptions.Build"/>.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_is_not_trusted_merely_because_the_framework_defaults_to_it(string candidate)
    {
        var built = new TrustedForwardersOptions { KnownNetworks = ["10.0.0.0/8"] }.Build();
        var address = IPAddress.Parse(candidate);

        Assert.DoesNotContain(built.KnownIPNetworks, network => network.Contains(address));
        Assert.DoesNotContain(address, built.KnownProxies);
    }
}
