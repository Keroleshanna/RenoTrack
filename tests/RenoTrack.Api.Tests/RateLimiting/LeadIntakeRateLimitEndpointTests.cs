using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Api.RateLimiting;

namespace RenoTrack.Api.Tests.RateLimiting;

/// <summary>
/// The contact form's own rate limiter over real HTTP (Phase 13 Slice 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this class can prove:</b> that <c>POST /api/v1/leads</c> is throttled at all — it was
/// not, and it is the only anonymous endpoint in the API that <em>writes</em> — that it binds at the
/// configured limit, that a rejection is a well-formed 429 carrying <c>Retry-After</c>, and that the
/// policy is its own bucket rather than the token-link surface's.
/// </para>
/// <para>
/// <b>What it cannot prove:</b> that two different clients get separate allowances.
/// <c>TestServer</c> supplies no <c>RemoteIpAddress</c>, so every request here shares the "unknown"
/// partition — which is why these tests bind at all without any address juggling, and why
/// partitioning itself is proven in <c>PublicRateLimitPartitionTests</c> instead. The same note
/// applies as in <c>PublicRateLimitEndpointTests</c>.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed class LeadIntakeRateLimitEndpointTests(RenoTrackApiFactory factory)
{
    private const int TestPermitLimit = 3;

    private WebApplicationFactoryHandle ThrottledFactory() =>
        new(factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                $"{LeadIntakeRateLimitOptions.SectionName}:{nameof(LeadIntakeRateLimitOptions.PermitLimit)}",
                TestPermitLimit.ToString())));

    private static object Submission() => new
    {
        name = "Throttle Case",
        phone = "+49 151 99999999",
        email = $"throttle-{Guid.NewGuid():N}@example.de",
    };

    [Fact]
    public async Task Submissions_below_the_limit_all_succeed()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/leads", Submission());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_submission_past_the_allowance_is_rejected_with_429_and_retry_after()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.PostAsJsonAsync("/api/v1/leads", Submission());
        }

        var rejected = await client.PostAsJsonAsync("/api/v1/leads", Submission());

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);

        // RFC 7807, like every other error this API returns — written through
        // IProblemDetailsService rather than hand-serialised.
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)HttpStatusCode.TooManyRequests, problem.GetProperty("status").GetInt32());
        Assert.Equal("Too Many Requests", problem.GetProperty("title").GetString());
    }

    /// <summary>
    /// The window reported to a throttled form submitter is the form's window, not the token-link
    /// surface's. Reading the wrong policy's window here would understate it by an order of
    /// magnitude — ten minutes told to come back in one.
    /// </summary>
    [Fact]
    public async Task The_retry_after_reflects_the_contact_forms_own_window()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit; i++)
        {
            await client.PostAsJsonAsync("/api/v1/leads", Submission());
        }

        var rejected = await client.PostAsJsonAsync("/api/v1/leads", Submission());
        var retryAfter = rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds;

        Assert.True(
            retryAfter > PublicRateLimitOptions.DefaultWindowSeconds,
            $"Retry-After was {retryAfter}s, which is the token-link window rather than the form's.");
        Assert.True(retryAfter <= LeadIntakeRateLimitOptions.DefaultWindowSeconds);
    }

    /// <summary>
    /// Two policies, two buckets: exhausting the form's allowance must leave the token-link surface
    /// answering, or spam against the form would take a customer's own quote offline.
    /// </summary>
    [Fact]
    public async Task Exhausting_the_form_does_not_throttle_the_token_link_surface()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit + 1; i++)
        {
            await client.PostAsJsonAsync("/api/v1/leads", Submission());
        }

        // 404 because the token is not real — the point is that it was not rejected as 429.
        var publicSurface = await client.GetAsync("/api/v1/public/angebote/unknown-token");

        Assert.Equal(HttpStatusCode.NotFound, publicSurface.StatusCode);
    }

    /// <summary>
    /// The policy covers the anonymous form only. The Admin's manual-entry route is a different
    /// endpoint with a different allowance — an Admin logging calls must never be throttled by
    /// whatever the public form has been receiving.
    /// </summary>
    [Fact]
    public async Task The_admins_manual_entry_route_is_not_covered_by_the_forms_policy()
    {
        using var throttled = ThrottledFactory();
        using var client = throttled.Value.CreateClient();

        for (var i = 0; i < TestPermitLimit + 1; i++)
        {
            await client.PostAsJsonAsync("/api/v1/leads", Submission());
        }

        // Unauthenticated, so 401 — the point is that the request reached authorization at all
        // rather than being rejected as 429 by the form's exhausted bucket.
        var manual = await client.PostAsJsonAsync("/api/v1/leads/manual", Submission());

        Assert.Equal(HttpStatusCode.Unauthorized, manual.StatusCode);
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
}
