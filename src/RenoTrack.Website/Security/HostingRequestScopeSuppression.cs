using Microsoft.Extensions.Options;

namespace RenoTrack.Website.Security;

/// <summary>
/// Turns off ASP.NET Core's hosting diagnostics logger for every logging provider, which is what
/// stops the framework creating the per-request logging scope that carries <c>RequestPath</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> For every request, ASP.NET's hosting layer opens a logging scope holding
/// <c>RequestId</c> and <c>RequestPath</c>. On <c>/angebot/{token}</c> that path <i>is</i> the
/// customer's credential, and every entry logged while the request runs carries it. The shipped
/// <c>Microsoft.AspNetCore: Warning</c> hides the request <i>lines</i> but not the scope, which is
/// attached to warnings and errors from any category. A sink that writes scopes then records the
/// token. On Windows that sink exists by default: <c>WebApplication.CreateBuilder</c> registers the
/// EventLog provider, which writes scopes into the Application event log. This was found by reading
/// that log after a test run, not by review.
/// </para>
/// <para>
/// <b>How it removes the scope rather than only the messages.</b> The hosting layer creates the scope
/// only when its logger, category <see cref="Category"/>, is enabled for at least one provider. Once
/// that logger is <see cref="LogLevel.None"/> for every provider, the scope is never created, so no
/// entry from any category can carry it. Hosting's request start and finish lines go with it; the
/// shipped configuration already suppressed those.
/// </para>
/// <para>
/// <b>What else it would take with it, measured rather than assumed, and why that is not left so.</b>
/// With no hosting logger, ASP.NET also skips creating the per-request <c>Activity</c> unless
/// something listens to its source. On its own this class would therefore strip <c>TraceId</c> and
/// <c>SpanId</c> from every log entry. <see cref="RequestActivityTracing"/> restores the Activity
/// without restoring the scope, because ASP.NET decides the two separately. <c>RequestId</c> is not
/// restored: it lived only in the scope removed here.
/// </para>
/// <para>
/// <b>Why a rule per provider, and why post-configuration.</b> A provider-specific logging rule always
/// beats a provider-less one, whatever its category. The host adds such a rule for EventLog
/// ("Warning and above"), so a plain <c>AddFilter(Category, LogLevel.None)</c> would leave EventLog's
/// hosting logger enabled and the scope alive. That was proven by trying it. This class therefore adds a
/// rule for each registered provider, plus a provider-less one. It runs as post-configuration, so its
/// rules come after every rule read from <c>appsettings.json</c> or the environment. Among rules of
/// equal specificity the later one wins, so no configuration value can turn this logger back on,
/// including one written for this exact provider and category.
/// </para>
/// <para>
/// This is the Website's copy of the same protection in <c>RenoTrack.Api</c>. The two are
/// deliberately not shared, because the Website references no backend project (<c>CLAUDE.md</c> §1).
/// </para>
/// </remarks>
internal sealed class HostingRequestScopeSuppression(IEnumerable<ILoggerProvider> providers)
    : IPostConfigureOptions<LoggerFilterOptions>
{
    /// <summary>The category whose enablement decides whether the request scope is created.</summary>
    internal const string Category = "Microsoft.AspNetCore.Hosting.Diagnostics";

    public void PostConfigure(string? name, LoggerFilterOptions options)
    {
        options.Rules.Add(new LoggerFilterRule(providerName: null, Category, LogLevel.None, filter: null));

        foreach (var providerType in providers.Select(provider => provider.GetType()).Distinct())
        {
            options.Rules.Add(new LoggerFilterRule(providerType.FullName, Category, LogLevel.None, filter: null));
        }
    }
}
