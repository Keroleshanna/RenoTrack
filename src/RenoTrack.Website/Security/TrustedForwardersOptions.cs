using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RenoTrack.Website.Security;

/// <summary>
/// The explicit allowlist of proxies whose <c>X-Forwarded-*</c> headers this Website will believe,
/// bound from the <c>TrustedForwarders</c> configuration section (D97).
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty means trust nothing, and empty is the default.</b> ASP.NET Core's own
/// <c>ForwardedHeadersOptions</c> ships with loopback pre-trusted; this type clears that and adds
/// only what an operator names, so a deployment that has configured nothing behaves exactly as the
/// system did before D97 rather than quietly believing a header. For a trust boundary, "fails
/// closed when unconfigured" is the only safe default — the same stance
/// <c>LeadsController.RequestingInspectorId()</c> takes on role scope, and the same reason
/// <c>DevelopmentBootstrap</c> refuses rather than assumes.
/// </para>
/// <para>
/// <b>Deliberately duplicated between this Website and the API</b> rather than shared. The Website
/// references no backend project (<c>CLAUDE.md</c> §1), and inventing a shared library to hold two
/// short lists would breach that boundary to save a few lines — the speculative abstraction §4
/// forbids, applied to configuration.
/// </para>
/// </remarks>
public sealed class TrustedForwardersOptions
{
    public const string SectionName = "TrustedForwarders";

    /// <summary>Individual proxy addresses, e.g. <c>10.0.0.7</c>.</summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>Proxy networks in CIDR form, e.g. <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>Whether any forwarder is trusted at all.</summary>
    public bool IsConfigured => KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    /// <summary>
    /// Builds the framework options, failing startup on a malformed entry rather than skipping it.
    /// A typo in a proxy address must not silently shrink the trust list — the result would be a
    /// limiter that looks configured and is not.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not a valid address or network.</exception>
    public ForwardedHeadersOptions Build()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,

            // One hop by default: this Website expects exactly one reverse proxy in front of it.
            // A larger value would let a proxy that is trusted for one hop assert a chain.
            ForwardLimit = 1,
        };

        // The framework pre-trusts loopback. Cleared, so the only trusted forwarders are the ones
        // configuration names.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"Configuration '{SectionName}:{nameof(KnownProxies)}' contains '{proxy}', which is not a " +
                    "valid IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in KnownNetworks)
        {
            // System.Net.IPNetwork.TryParse, fully qualified: `IPNetwork` also exists in
            // Microsoft.AspNetCore.HttpOverrides (imported here for the ForwardedHeaders flags), and
            // that older type is what the now-obsolete KnownNetworks property held. KnownIPNetworks
            // is its replacement and takes the System.Net type.
            //
            // TryParse rather than hand-splitting on '/': it rejects a non-canonical network such as
            // "10.0.0.1/8" that a manual parse would silently accept and then mis-apply, and there is
            // no reason to keep a second, weaker CIDR parser in this codebase.
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration '{SectionName}:{nameof(KnownNetworks)}' contains '{network}', which is not a " +
                    "valid CIDR network, e.g. '10.0.0.0/8'. The address must be the network itself, with no bits " +
                    "set beyond the prefix length.");
            }

            options.KnownIPNetworks.Add(parsed);
        }

        return options;
    }
}
