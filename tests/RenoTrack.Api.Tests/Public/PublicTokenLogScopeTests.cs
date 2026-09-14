using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Api.Security;

namespace RenoTrack.Api.Tests.Public;

/// <summary>
/// That the customer's token does not ride on ASP.NET's per-request logging scope on the public
/// token routes, as <c>RequestPath</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this adds over <c>PublicAngebotViewEndpointTests.The_token_never_reaches_the_application_log</c>.</b>
/// That test proves <c>RouteDiagnostics</c> keeps the token out of the formatted message. It cannot
/// see scopes, and the framework creates the request scope before routing runs, so redaction never
/// reaches it. Every warning logged during a token request, such as the mapped 404 below, carried
/// the raw path in its scope. The Windows EventLog provider, registered by default, wrote it into the
/// Application event log. <see cref="HostingRequestScopeSuppression"/> stops the scope being created.
/// </para>
/// <para>
/// <b>The capturing provider gets a provider-specific rule re-enabling hosting diagnostics</b>, the
/// shape of EventLog's own default rule, so a weaker fix fails here rather than only on a host that
/// happens to have EventLog registered. A negative control removes the suppression and shows the
/// leak, so a green run cannot come from a capture that sees no scopes.
/// </para>
/// <para>
/// <b>Trace correlation must survive the suppression.</b> <see cref="RequestActivityTracing"/> keeps
/// ASP.NET creating the request Activity, so the ProblemDetails <c>traceId</c> stays W3C and matches
/// the <c>TraceId</c> on the same request's log entries. Both properties are asserted on one request.
/// Each request's Activity is read from the request itself, never through a test
/// <c>ActivityListener</c>, which would hide that class's removal.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed partial class PublicTokenLogScopeTests(RenoTrackApiFactory factory)
{
    private const string Token = "ApiScopeProbe-7Kq2Wc0KpL7sTvE1aZoI4hJd6UgXb";

    public static TheoryData<string> Requests =>
    [
        "read: unknown token",
        "decision: unknown token",
    ];

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task No_log_entry_or_scope_carries_the_token(string request)
    {
        var logs = (await SendAsync(request)).Logs;

        // Not vacuous: the refusal is logged inside the request, which is exactly where a scope rides.
        Assert.Contains(
            logs.Entries,
            entry => entry.Level >= LogLevel.Warning
                && entry.Category == typeof(ProblemDetailsExceptionHandler).FullName);

        var offenders = logs.Entries.Where(entry => entry.Contains(Token)).ToList();
        Assert.True(offenders.Count == 0, Describe(offenders));
    }

    /// <summary>
    /// Configuration cannot bring the scope back, whether hosting diagnostics are re-enabled globally
    /// or for one provider.
    /// </summary>
    [Fact]
    public async Task Configuration_that_re_enables_hosting_diagnostics_does_not_bring_the_token_back()
    {
        var logs = (await SendAsync(
            "read: unknown token",
            extraSettings: new Dictionary<string, string?>
            {
                [$"Logging:LogLevel:{HostingRequestScopeSuppression.Category}"] = "Trace",
            })).Logs;

        var offenders = logs.Entries.Where(entry => entry.Contains(Token)).ToList();
        Assert.True(offenders.Count == 0, Describe(offenders));
    }

    /// <summary>
    /// With the suppression removed and nothing else changed, the mapped 404's warning carries the
    /// token in its scope. This reproduces the defect and proves the assertions above can see it.
    /// </summary>
    [Fact]
    public async Task Control_without_the_scope_suppression_the_request_scope_carries_the_token()
    {
        var logs = (await SendAsync("read: unknown token", withoutRequestScopeSuppression: true)).Logs;

        Assert.Contains(logs.Entries, entry => entry.Level >= LogLevel.Warning && entry.ScopesContain(Token));
    }

    // ---- Trace correlation survives the scope suppression ---------------------

    /// <summary>
    /// Both properties on one request: the ProblemDetails <c>traceId</c> is W3C and its trace-id
    /// segment is the <c>TraceId</c> on the refusal's log entry, and no scope on any entry carries
    /// <c>RequestPath</c> or the token.
    /// </summary>
    [Theory]
    [MemberData(nameof(Requests))]
    public async Task The_problem_details_trace_id_is_w3c_and_matches_the_log_entry_without_a_request_path(string request)
    {
        var observed = await SendAsync(request);

        using var problem = JsonDocument.Parse(observed.Body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString() ?? string.Empty;
        var w3c = W3CTraceParent().Match(traceId);
        Assert.True(w3c.Success, $"traceId '{traceId}' is not W3C trace-context form.");

        var refusal = Assert.Single(
            observed.Logs.Entries,
            entry => entry.Level >= LogLevel.Warning
                && entry.Category == typeof(ProblemDetailsExceptionHandler).FullName);
        Assert.Equal(w3c.Groups["trace"].Value, refusal.ScopeValue("TraceId"));
        Assert.Matches("^[0-9a-f]{16}$", refusal.ScopeValue("SpanId") ?? string.Empty);

        Assert.DoesNotContain(observed.Logs.Entries, entry => entry.HasScopeKey("RequestPath"));
        Assert.DoesNotContain(observed.Logs.Entries, entry => entry.Contains(Token));
    }

    /// <summary>
    /// ASP.NET creates the request Activity again, and nothing on it carries the token: not its tags,
    /// its baggage, or its name.
    /// </summary>
    [Theory]
    [MemberData(nameof(Requests))]
    public async Task The_request_activity_exists_and_carries_no_token(string request)
    {
        var observed = await SendAsync(request);

        var activity = Assert.Single(observed.Activities);
        Assert.NotNull(activity);
        Assert.NotEqual(default, activity.TraceId);

        var visible = new[] { activity.DisplayName, activity.OperationName }
            .Concat(activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))
            .Concat(activity.Baggage.Select(item => $"{item.Key}={item.Value}"));
        Assert.DoesNotContain(visible, text => text.Contains(Token, StringComparison.Ordinal));
    }

    [GeneratedRegex("^00-(?<trace>[0-9a-f]{32})-(?<span>[0-9a-f]{16})-[0-9a-f]{2}$")]
    private static partial Regex W3CTraceParent();

    private sealed record Observed(ScopeCapturingLoggerProvider Logs, string Body, IReadOnlyList<Activity?> Activities);

    private async Task<Observed> SendAsync(
        string request,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        bool withoutRequestScopeSuppression = false)
    {
        var logs = new ScopeCapturingLoggerProvider();
        var activities = new List<Activity?>();

        var settings = new Dictionary<string, string?>
        {
            [$"Logging:{typeof(ScopeCapturingLoggerProvider).FullName}:LogLevel:{HostingRequestScopeSuppression.Category}"] = "Trace",
        };
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                if (withoutRequestScopeSuppression)
                {
                    services.Remove(services.Single(descriptor =>
                        descriptor.ServiceType == typeof(IPostConfigureOptions<LoggerFilterOptions>)
                        && descriptor.ImplementationType == typeof(HostingRequestScopeSuppression)));

                    // The control reproduces a real leak, so it must not reach a real sink. On Windows
                    // the host registers the EventLog provider by default, and without this the control
                    // would write its probe token into the machine's Application event log on every run.
                    services.RemoveAll<ILoggerProvider>();
                }

                services.AddSingleton<ILoggerProvider>(logs);
                services.AddSingleton<IStartupFilter>(new RequestActivityRecorder(activities));
            });
        });

        // Over HTTPS, as the Website calls it; over HTTP the redirection middleware adds a warning of
        // its own that belongs to the test host, not to a deployment.
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = request switch
        {
            "read: unknown token" => await client.GetAsync($"/api/v1/public/angebote/{Token}"),
            "decision: unknown token" => await client.PostAsJsonAsync(
                $"/api/v1/public/angebote/{Token}/decision", new { decision = "Approve" }),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, "Unknown request."),
        };

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        lock (activities)
        {
            return new Observed(logs, body, [.. activities]);
        }
    }

    /// <summary>
    /// Records each request's Activity from the outermost middleware position. The object is kept, so
    /// tags added when the Activity stops are visible afterwards.
    /// </summary>
    private sealed class RequestActivityRecorder(List<Activity?> activities) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                lock (activities)
                {
                    activities.Add(context.Features.Get<IHttpActivityFeature>()?.Activity);
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }

    private static string Describe(IReadOnlyList<ScopeCapturingLoggerProvider.Entry> offenders) =>
        $"The customer's token reached {offenders.Count} log entr{(offenders.Count == 1 ? "y" : "ies")}:"
        + Environment.NewLine
        + string.Join(Environment.NewLine, offenders.Select(entry => entry.ToString().Replace(Token, "<TOKEN>", StringComparison.Ordinal)));

    /// <summary>
    /// Captures message, exception, structured state and ambient scopes: everything a sink can write.
    /// It does not filter, so what arrives is decided by the host's own logging rules.
    /// </summary>
    private sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly List<Entry> _entries = [];
        private readonly Lock _sync = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        internal sealed record Entry(string Category, LogLevel Level, string Text, IReadOnlyList<string> Scopes)
        {
            public bool ScopesContain(string value) => Scopes.Any(scope => scope.Contains(value, StringComparison.Ordinal));

            public bool HasScopeKey(string key) => ScopeValue(key) is not null;

            /// <summary>The value of <paramref name="key"/> in the innermost scope that carries it, if any.</summary>
            public string? ScopeValue(string key)
            {
                foreach (var scope in Scopes.Reverse())
                {
                    foreach (var pair in scope.Split(", "))
                    {
                        if (pair.StartsWith($"{key}=", StringComparison.Ordinal))
                        {
                            return pair[(key.Length + 1)..];
                        }
                    }
                }

                return null;
            }

            public bool Contains(string value) => Text.Contains(value, StringComparison.Ordinal) || ScopesContain(value);

            public override string ToString() => $"[{Level}] {Category}: {Text} | scopes: {string.Join(" => ", Scopes)}";
        }

        public IReadOnlyList<Entry> Entries
        {
            get { lock (_sync) { return [.. _entries]; } }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ScopeCapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var stateText = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                    : string.Empty;

                var scopes = new List<string>();
                owner._scopes.ForEachScope(
                    (scope, list) => list.Add(scope is IEnumerable<KeyValuePair<string, object?>> scopePairs
                        ? string.Join(", ", scopePairs.Select(pair => $"{pair.Key}={pair.Value}"))
                        : scope?.ToString() ?? string.Empty),
                    scopes);

                lock (owner._sync)
                {
                    owner._entries.Add(new Entry(category, logLevel, $"{formatter(state, exception)} | state: {stateText} | exception: {exception}", scopes));
                }
            }
        }
    }
}
