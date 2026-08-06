using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Api.RateLimiting;

namespace RenoTrack.Api.Tests.RateLimiting;

/// <summary>
/// The public-surface rate limiter over real HTTP (Architecture.md §12, D65).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class can prove:</b> that the limiter is actually in the pipeline, that it binds at
/// the configured permit limit, that GET and POST share one allowance, that a rejection is a
/// well-formed 429 with <c>Retry-After</c>, that the token survives nowhere in that response or in
/// the log, and that internal routes are untouched by the policy.
/// </para>
/// <para>
/// <b>What it cannot prove:</b> that two different clients get separate allowances.
/// <c>TestServer</c> supplies no <c>RemoteIpAddress</c>, so every request here shares the "unknown"
/// partition — which is exactly why these tests bind at all without any IP juggling. Faking a
/// remote address would mean simulating the framework behaviour under test. Partitioning is
/// therefore proven at unit level in <c>PublicRateLimitPartitionTests</c>, against real
/// <c>HttpContext</c> instances with real addresses.
/// </para>
/// <para>
/// The limit is lowered to a handful of requests rather than waiting out a real window — the window
/// is configuration, and no assertion here depends on wall-clock expiry.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed class PublicRateLimitEndpointTests(RenoTrackApiFactory factory)
{
    private const int TestPermitLimit = 3;

    private WebApplicationFactoryHandle ThrottledFactory(ILoggerProvider? logs = null) =>
        new(factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                $"{PublicRateLimitOptions.SectionName}:{nameof(PublicRateLimitOptions.PermitLimit)}",
                TestPermitLimit.ToString());

            if (logs is not null)
            {
                builder.ConfigureServices(services => services.AddSingleton(logs));
            }
        }));

    [Fact]
    public async Task Requests_below_the_limit_all_succeed()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit; i++)
        {
            var response = await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");

            // 404 because the token is not real — the point is that it reached the handler at all.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_request_past_the_allowance_is_rejected_with_429()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();
        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");
        }

        var response = await client.GetAsync("/api/v1/public/angebote/one-too-many");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    /// <summary>
    /// One shared policy across the whole public surface (D65 §5): a caller cannot exhaust the GET
    /// allowance and then keep POSTing decisions.
    /// </summary>
    [Fact]
    public async Task Get_and_post_consume_the_same_allowance()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();
        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");
        }

        var decision = await client.PostAsJsonAsync(
            "/api/v1/public/angebote/some-token/decision", new { decision = "Approve" });

        Assert.Equal(HttpStatusCode.TooManyRequests, decision.StatusCode);
    }

    /// <summary>
    /// CLAUDE.md §22: every error leaves this API as RFC 7807, and a 429 is no exception — it is
    /// written through IProblemDetailsService rather than hand-rolled, which is also what puts
    /// traceId on it.
    /// </summary>
    [Fact]
    public async Task The_rejection_is_rfc7807_problem_details_with_retry_after()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();
        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");
        }

        var response = await client.GetAsync("/api/v1/public/angebote/one-too-many");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.RetryAfter);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    /// <summary>
    /// Slice 3's security property must survive the new response path: a throttled token request
    /// must not leak the token it was throttled for, in the body or in the log.
    /// </summary>
    [Fact]
    public async Task A_throttled_token_request_leaks_the_token_nowhere()
    {
        const string token = "throttled-secret-token";
        var logs = new CapturingLoggerProvider();
        using var throttled = ThrottledFactory(logs);
        using var client = throttled.Value.CreateClient();
        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");
        }

        var response = await client.GetAsync($"/api/v1/public/angebote/{token}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);

        var problem = JsonDocument.Parse(raw).RootElement;
        Assert.DoesNotContain(token, problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(token, problem.GetProperty("instance").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>
    /// The policy is opt-in per endpoint, not global. Without this, tightening the public limit
    /// would silently throttle the Dashboard too — and the failure would look like an outage.
    /// </summary>
    [Fact]
    public async Task Internal_routes_are_not_covered_by_the_public_policy()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();
        for (var i = 0; i < TestPermitLimit + 5; i++)
        {
            await client.GetAsync($"/api/v1/public/angebote/unknown-token-{i}");
        }

        using var admin = await AdminClientAsync(throttled.Value);
        for (var i = 0; i < TestPermitLimit + 5; i++)
        {
            var response = await admin.GetAsync("/api/v1/angebote/999999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = RenoTrackApiFactory.AdminEmail, password = RenoTrackApiFactory.AdminPassword });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    /// <summary>
    /// <c>WithWebHostBuilder</c> returns a factory that owns a second host; disposing it releases
    /// that host without touching the shared fixture's database lifetime.
    /// </summary>
    private sealed class WebApplicationFactoryHandle(WebApplicationFactory<Program> value) : IDisposable
    {
        public WebApplicationFactory<Program> Value { get; } = value;

        public void Dispose() => Value.Dispose();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public List<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
