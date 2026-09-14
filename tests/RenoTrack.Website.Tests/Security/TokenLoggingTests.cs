using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;

namespace RenoTrack.Website.Tests.Security;

/// <summary>
/// That a customer's token reaches no log sink, through any of the three ways this Website could
/// write one: its own outbound calls to the API, ASP.NET's inbound request logging, and the
/// per-request logging scope ASP.NET attaches to every entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each surface has its own protection, and each test is built so it fails when that protection
/// goes.</b>
/// </para>
/// <list type="bullet">
/// <item><b>Outbound.</b> <c>IHttpClientFactory</c>'s logging handlers are removed structurally by
/// <c>RemoveAllLoggers()</c> in <c>Program.cs</c>. These tests raise <c>System.Net.Http.HttpClient</c>
/// to <c>Trace</c>, so only that removal stands between the token and the log.</item>
/// <item><b>Inbound.</b> Everything else under <c>Microsoft.AspNetCore</c> is kept quiet by the
/// shipped <c>Microsoft.AspNetCore: Warning</c>. These tests leave that level as shipped, so a change
/// to it in <c>appsettings.json</c> is exercised.</item>
/// <item><b>The request scope.</b> ASP.NET opens a scope holding <c>RequestPath</c> for every
/// request. <see cref="HostingRequestScopeSuppression"/> stops that scope being created. These tests
/// add a provider-specific rule that re-enables hosting diagnostics for the capturing provider, which
/// is the shape of the EventLog provider's own default rule on Windows. That makes a weaker fix, such
/// as a plain category filter, fail here on every operating system rather than only on Windows.</item>
/// <item><b>Trace correlation, which must survive the suppression.</b> <c>RequestActivityTracing</c>
/// keeps ASP.NET creating the request Activity. These tests assert <c>TraceId</c>/<c>SpanId</c> on
/// the same entries whose scopes must carry no <c>RequestPath</c>. They read each request's Activity
/// from the request itself, never through a test <c>ActivityListener</c>, which would hide that
/// class's removal.</item>
/// </list>
/// <para>
/// <b>The controls prove the harness can see a leak</b>, so a green result is not the product of a
/// capture that sees nothing: a client without <c>RemoveAllLoggers()</c> does log the token, and a
/// host without the scope suppression does attach it.
/// </para>
/// <para>
/// Everything a sink can write is inspected: the formatted message, the exception, the structured
/// state values, and the ambient scopes.
/// </para>
/// </remarks>
public sealed partial class TokenLoggingTests
{
    private const string Token = "Lg7q-TokenLogProbe_x2Wc0KpL7sTvE1aZoI4hJd6U";
    private const string DocumentUrl = $"/angebot/{Token}";
    private const string ApproveUrl = $"/angebot/{Token}/entscheidung/annehmen";
    private const string RejectUrl = $"/angebot/{Token}/entscheidung/ablehnen";

    /// <summary>
    /// A provider-specific rule for the capturing provider, written against the category that decides
    /// whether ASP.NET creates the request scope.
    /// </summary>
    private static readonly string CaptureHostingDiagnosticsKey =
        $"{typeof(CapturingLoggerProvider).FullName}:LogLevel:{HostingRequestScopeSuppression.Category}";

    /// <summary>
    /// Every level on, except <c>Microsoft.AspNetCore</c>, which this project suppresses by
    /// configuration and the tests must therefore leave as shipped. Hosting diagnostics are also
    /// re-enabled for the capturing provider specifically, as EventLog's default rule does on Windows.
    /// Keys are relative to the <c>Logging</c> section.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> Verbose = new Dictionary<string, string?>
    {
        ["LogLevel:Default"] = "Trace",
        ["LogLevel:System.Net.Http.HttpClient"] = "Trace",
        [CaptureHostingDiagnosticsKey] = "Trace",
    };

    public static TheoryData<string> Scenarios =>
    [
        "view: available",
        "view: not found (404)",
        "view: expired (410)",
        "view: unavailable (503)",
        "view: rejected as malformed (400)",
        "view: unreadable 200",
        "view: transport failure",
        "decision: approve recorded",
        "decision: reject with reason recorded",
        "decision: already decided (409)",
        "decision: unavailable (500)",
        "decision: transport failure",
    ];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task No_log_message_state_or_exception_carries_the_token(string scenario)
    {
        var logs = (await DriveAsync(scenario, Verbose)).Logs;

        var offenders = logs.Where(entry => entry.RecordedPartsContain(Token)).ToList();
        Assert.True(offenders.Count == 0, Describe(offenders));
    }

    /// <summary>
    /// Scopes are what a sink with scopes enabled writes beside every entry. The Windows EventLog
    /// provider, which <c>WebApplication.CreateBuilder</c> registers by default on Windows, is one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task No_log_scope_carries_the_token(string scenario)
    {
        var logs = (await DriveAsync(scenario, Verbose)).Logs;

        var offenders = logs.Where(entry => entry.ScopesContain(Token)).ToList();
        Assert.True(offenders.Count == 0, Describe(offenders));
    }

    /// <summary>
    /// The failure paths are the ones a scope would ride on, because they log inside the request.
    /// Asserting that they still log keeps the scope test from passing merely because nothing was
    /// written.
    /// </summary>
    [Theory]
    [InlineData("view: unavailable (503)")]
    [InlineData("view: transport failure")]
    [InlineData("decision: unavailable (500)")]
    public async Task The_failure_paths_still_log_a_warning_or_error_inside_the_request(string scenario)
    {
        var logs = (await DriveAsync(scenario, Verbose)).Logs;

        Assert.Contains(
            logs,
            entry => entry.Level >= LogLevel.Warning && entry.Category == typeof(PublicAngebotClient).FullName);
    }

    /// <summary>
    /// Configuration cannot bring the scope back. Hosting diagnostics are raised to <c>Trace</c> both
    /// globally and for the capturing provider specifically, the two shapes a well-meaning "turn
    /// request logging back on" change would take.
    /// </summary>
    [Fact]
    public async Task Configuration_that_re_enables_hosting_diagnostics_does_not_bring_the_token_back()
    {
        var levels = new Dictionary<string, string?>(Verbose)
        {
            [$"LogLevel:{HostingRequestScopeSuppression.Category}"] = "Trace",
        };

        var logs = (await DriveAsync("view: unavailable (503)", levels)).Logs;

        var offenders = logs
            .Where(entry => entry.RecordedPartsContain(Token) || entry.ScopesContain(Token))
            .ToList();
        Assert.True(offenders.Count == 0, Describe(offenders));
    }

    // ---- Trace correlation survives the scope suppression ---------------------

    /// <summary>
    /// Both properties on the same entries: the warning or error logged inside the request carries
    /// <c>TraceId</c> and <c>SpanId</c>, which match the request's own Activity, and no scope on any
    /// entry carries <c>RequestPath</c> or the token. <c>RequestActivityTracing</c> provides the first
    /// and <c>HostingRequestScopeSuppression</c> the second.
    /// </summary>
    [Theory]
    [InlineData("view: unavailable (503)")]
    [InlineData("view: transport failure")]
    [InlineData("decision: unavailable (500)")]
    public async Task Failure_path_entries_carry_trace_correlation_and_no_request_path(string scenario)
    {
        var observed = await DriveAsync(scenario, Verbose);

        var inRequest = observed.Logs
            .Where(entry => entry.Level >= LogLevel.Warning && entry.Category == typeof(PublicAngebotClient).FullName)
            .ToList();
        Assert.NotEmpty(inRequest);

        foreach (var entry in inRequest)
        {
            Assert.Matches(TraceIdPattern(), entry.ScopeValue("TraceId") ?? string.Empty);
            Assert.Matches(SpanIdPattern(), entry.ScopeValue("SpanId") ?? string.Empty);
        }

        var traceId = Assert.Single(inRequest.Select(entry => entry.ScopeValue("TraceId")).Distinct());
        Assert.Contains(
            observed.Activities,
            activity => activity is not null && activity.TraceId.ToHexString() == traceId);

        Assert.DoesNotContain(observed.Logs, entry => entry.HasScopeKey("RequestPath"));
        Assert.DoesNotContain(observed.Logs, entry => entry.ScopesContain(Token) || entry.RecordedPartsContain(Token));
    }

    /// <summary>
    /// ASP.NET creates the request Activity again, and nothing on it carries the token: not its tags,
    /// its baggage, or its name. Its tags would include <c>url.path</c> if ASP.NET's OpenTelemetry
    /// activity data were opted in, so this is also what fails if that is turned on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task The_request_activity_exists_and_carries_no_token(string scenario)
    {
        var observed = await DriveAsync(scenario, Verbose);

        Assert.NotEmpty(observed.Activities);
        foreach (var activity in observed.Activities)
        {
            Assert.NotNull(activity);
            Assert.NotEqual(default, activity.TraceId);

            var visible = new[] { activity.DisplayName, activity.OperationName }
                .Concat(activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))
                .Concat(activity.Baggage.Select(item => $"{item.Key}={item.Value}"));
            Assert.DoesNotContain(visible, text => text.Contains(Token, StringComparison.Ordinal));
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdPattern();

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex SpanIdPattern();

    // ---- Controls: the harness can see a leak on each surface ---------------

    /// <summary>
    /// At the same level, a client built without <c>RemoveAllLoggers()</c> does log the token. This
    /// is what proves the main tests' silence comes from the typed client's registration.
    /// </summary>
    [Fact]
    public async Task Control_an_http_client_without_RemoveAllLoggers_does_log_the_token()
    {
        using var factory = new TokenLoggingWebsiteFactory(_ => Json(HttpStatusCode.OK, "{}"), Verbose);

        var control = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("token-logging-control");
        using var response = await control.GetAsync(
            $"{TokenLoggingWebsiteFactory.ApiBaseUrl}/api/v1/public/angebote/{Token}");

        Assert.Contains(
            factory.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient.token-logging-control", StringComparison.Ordinal)
                && entry.RecordedPartsContain(Token));
    }

    /// <summary>
    /// With <see cref="HostingRequestScopeSuppression"/> removed and nothing else changed, the warning
    /// on a failing quote read carries the token in its scope. This reproduces the defect the
    /// suppression fixes, and proves the scope tests can see it.
    /// </summary>
    [Fact]
    public async Task Control_without_the_scope_suppression_the_request_scope_carries_the_token()
    {
        var logs = (await DriveAsync("view: unavailable (503)", Verbose, withoutRequestScopeSuppression: true)).Logs;

        Assert.Contains(logs, entry => entry.Level >= LogLevel.Warning && entry.ScopesContain(Token));
    }

    // ---- Driving the scenarios ---------------------------------------------

    private sealed record Observed(IReadOnlyList<CapturedLogEntry> Logs, IReadOnlyList<Activity?> Activities);

    private static async Task<Observed> DriveAsync(
        string scenario,
        IReadOnlyDictionary<string, string?> loggingOverrides,
        bool withoutRequestScopeSuppression = false)
    {
        var apiCalls = 0;
        var respond = Responder(scenario);

        using var factory = new TokenLoggingWebsiteFactory(
            request =>
            {
                Interlocked.Increment(ref apiCalls);
                return respond(request);
            },
            loggingOverrides,
            withoutRequestScopeSuppression);
        using var client = CreateClient(factory);

        if (scenario.StartsWith("decision:", StringComparison.Ordinal))
        {
            var url = scenario.Contains("reject", StringComparison.Ordinal) ? RejectUrl : ApproveUrl;
            var reason = scenario.Contains("reason", StringComparison.Ordinal) ? "Zu teuer für uns" : null;
            await PostConfirmationAsync(client, url, reason);
        }
        else
        {
            using var response = await client.GetAsync(DocumentUrl);
        }

        // The real typed client made the call. Without this, a scenario that never reached the API
        // would pass for the wrong reason.
        Assert.True(apiCalls > 0, $"Scenario '{scenario}' never reached the API client.");
        Assert.NotEmpty(factory.Logs.Entries);

        return new Observed(factory.Logs.Entries, factory.RequestActivities);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Responder(string scenario)
    {
        HttpResponseMessage Document() => Json(HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson());

        return scenario switch
        {
            "view: available" => _ => Document(),
            "view: not found (404)" => _ => Json(HttpStatusCode.NotFound, "{}"),
            "view: expired (410)" => _ => Json(HttpStatusCode.Gone, "{}"),
            "view: unavailable (503)" => _ => Json(HttpStatusCode.ServiceUnavailable, "{}"),
            "view: rejected as malformed (400)" => _ => Json(HttpStatusCode.BadRequest, "{}"),
            "view: unreadable 200" => _ => Json(HttpStatusCode.OK, "{}"),
            "view: transport failure" => _ => throw new HttpRequestException("Connection refused."),
            "decision: approve recorded" or "decision: reject with reason recorded" =>
                request => request.Method == HttpMethod.Post ? Json(HttpStatusCode.OK, "{}") : Document(),
            "decision: already decided (409)" =>
                request => request.Method == HttpMethod.Post ? Json(HttpStatusCode.Conflict, "{}") : Document(),
            "decision: unavailable (500)" =>
                request => request.Method == HttpMethod.Post ? Json(HttpStatusCode.InternalServerError, "{}") : Document(),
            "decision: transport failure" =>
                request => request.Method == HttpMethod.Post
                    ? throw new HttpRequestException("Connection refused.")
                    : Document(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };
    }

    /// <summary>
    /// Over HTTPS, as a customer arrives in production. Over plain HTTP the redirection middleware
    /// logs a warning of its own on every request, which is a property of the test host rather than
    /// of a deployment.
    /// </summary>
    private static HttpClient CreateClient(TokenLoggingWebsiteFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>GET the confirmation, take its antiforgery token, POST it back — as a browser does.</summary>
    private static async Task PostConfirmationAsync(HttpClient client, string url, string? reason)
    {
        var html = await client.GetStringAsync(url);
        var antiforgery = AntiforgeryField().Match(html);
        Assert.True(antiforgery.Success, "The confirmation form carried no antiforgery token.");

        List<KeyValuePair<string, string>> fields = [new("__RequestVerificationToken", antiforgery.Groups[1].Value)];
        if (reason is not null)
        {
            fields.Add(new KeyValuePair<string, string>("Reason", reason));
        }

        using var response = await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static string Describe(IReadOnlyList<CapturedLogEntry> offenders) =>
        $"The customer's token reached {offenders.Count} log entr{(offenders.Count == 1 ? "y" : "ies")}:"
        + Environment.NewLine
        + string.Join(Environment.NewLine, offenders.Select(entry => entry.ToString().Replace(Token, "<TOKEN>", StringComparison.Ordinal)));

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
