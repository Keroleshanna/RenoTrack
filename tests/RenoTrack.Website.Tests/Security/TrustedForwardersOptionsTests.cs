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

    /// <summary>
    /// <c>10.0.0.1/8</c> is included deliberately: it looks plausible and is not a network. Parsing
    /// through <c>System.Net.IPNetwork</c> rejects an address with bits set beyond the prefix, where
    /// a hand-rolled split on <c>/</c> would have accepted it and then matched against something the
    /// operator did not write.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/notanumber")]
    [InlineData("garbage/8")]
    [InlineData("10.0.0.0/8/9")]
    [InlineData("10.0.0.1/8")]
    [InlineData("")]
    public void A_malformed_network_fails_startup_naming_the_key(string network)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new TrustedForwardersOptions { KnownNetworks = [network] }.Build());

        Assert.Contains(nameof(TrustedForwardersOptions.KnownNetworks), exception.Message, StringComparison.Ordinal);
    }
}
