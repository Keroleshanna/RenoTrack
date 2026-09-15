using System.Diagnostics;

namespace RenoTrack.Api.Security;

/// <summary>
/// Keeps ASP.NET Core creating its per-request <see cref="Activity"/>, so log entries carry
/// <c>TraceId</c> and <c>SpanId</c>, after <see cref="HostingRequestScopeSuppression"/> has turned
/// the hosting logger off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> ASP.NET's hosting layer creates the request Activity when its hosting
/// logger is enabled, <i>or</i> a diagnostic listener asks for one, <i>or</i> its
/// <c>Microsoft.AspNetCore</c> activity source has a listener. The scope suppression removes the
/// first reason, and nothing else supplied the other two, so trace correlation disappeared with the
/// token-bearing scope. Measured, not assumed. One listener on that source restores the Activity
/// alone.
/// </para>
/// <para>
/// <b>Why it cannot bring the token back.</b> ASP.NET makes the two decisions separately. The
/// <c>RequestId</c>/<c>RequestPath</c> scope is created only when the hosting logger is enabled,
/// which the suppression keeps at <c>None</c> for every provider; this listener never touches a
/// logging rule. What flows from the Activity into log scopes is its ids, which are random hex. Its
/// tags would include <c>url.path</c> only if ASP.NET's OpenTelemetry activity data were opted in,
/// which it is not by default. Even then, tags reach no log, because the logger's activity tracking
/// covers ids only, and nothing here exports them. A test pins that the Activity's tags, baggage and
/// name carry no token, so opting in later fails loudly.
/// </para>
/// <para>
/// <b>Deliberately minimal.</b> One source, no exporter, no OpenTelemetry, and
/// <see cref="ActivitySamplingResult.PropagationData"/>: the Activity gets its ids and records
/// nothing. The sampler ignores its input, so it never reads request data. The listener is disposed
/// when the application stops, so repeated hosts in one process, as in tests, do not accumulate
/// listeners.
/// </para>
/// <para>
/// <c>RequestId</c> is not restored. It only ever existed in the same framework scope object as
/// <c>RequestPath</c>; <c>TraceId</c> is the per-request correlation key instead.
/// </para>
/// <para>
/// This is the API's copy; <c>RenoTrack.Website</c> carries its own, because the Website references
/// no backend project (<c>CLAUDE.md</c> §1).
/// </para>
/// </remarks>
internal static class RequestActivityTracing
{
    /// <summary>The activity source ASP.NET Core's hosting layer creates request activities from.</summary>
    internal const string SourceName = "Microsoft.AspNetCore";

    internal static void Enable(WebApplication app)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
        };

        ActivitySource.AddActivityListener(listener);
        app.Lifetime.ApplicationStopped.Register(listener.Dispose);
    }
}
