using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// That the ASP.NET scaffold is gone, and that the one page it left behind which a customer can
/// actually reach is now customer-safe.
/// </summary>
/// <remarks>
/// <para>
/// <b>A deletion with no test can be undone by a scaffold regeneration and nobody would notice.</b>
/// These routes served real 200s on the customer-facing origin until Slice 7 — an English "Welcome /
/// Learn about building Web apps with ASP.NET Core" at <c>/</c>, and an English "Use this page to
/// detail your site's privacy policy" at <c>/Privacy</c>, which is the exact placeholder FR-1.4
/// exists to replace.
/// </para>
/// <para>
/// <b>This is not Phase 13's marketing site being refused.</b> A1/A2 remain deferred (Q6); what is
/// asserted here is only that the template's own pages and its jQuery/Bootstrap assets are not
/// served.
/// </para>
/// </remarks>
public sealed class ScaffoldRemovalTests(CustomerWebsiteFactory factory) : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    [Theory]
    [InlineData("/")]
    [InlineData("/Index")]
    [InlineData("/Privacy")]
    public async Task The_scaffold_pages_are_gone(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/js/site.js")]
    [InlineData("/css/site.css")]
    [InlineData("/lib/jquery/dist/jquery.min.js")]
    [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/favicon.ico")]
    public async Task The_scaffold_assets_are_gone(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The stylesheet the customer pages actually use is untouched by the removal.</summary>
    [Fact]
    public async Task The_customer_stylesheet_is_still_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/css/customer.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- The error page a customer can actually reach ----------------------

    /// <summary>
    /// <b>Driven through a real unhandled exception, not by requesting <c>/Error</c> directly.</b>
    /// The way a customer reaches this page is <c>UseExceptionHandler</c> re-executing into it from
    /// <c>/angebot/{token}</c>, and that is the path worth testing: it is what put the scaffold's
    /// English page, its jQuery and Bootstrap script tags, its links into the template site, and a
    /// request identifier in front of a customer whose quote had failed to load.
    /// </summary>
    [Fact]
    public async Task An_unhandled_failure_on_a_quote_page_shows_a_customer_safe_german_error()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPublicAngebotClient>();
                services.AddSingleton<IPublicAngebotClient>(new ThrowingPublicAngebotClient());
            }));
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // German, and the customer layout — not the scaffold's English page.
        Assert.Contains("Es ist ein Fehler aufgetreten", html, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred while processing your request", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Development Mode", html, StringComparison.Ordinal);

        // CLAUDE.md §24: no script element on a customer page at all.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        // No internal identifier of any kind reaches the customer.
        Assert.DoesNotContain("Request ID", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestId", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("traceId", html, StringComparison.OrdinalIgnoreCase);

        // The exception's own message must never surface.
        Assert.DoesNotContain(ThrowingPublicAngebotClient.Message, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The credential must not survive into the error response, in the body or in any header — the
    /// browser is still sitting on the token URL when this renders.
    /// </summary>
    [Fact]
    public async Task The_error_response_leaks_neither_the_token_nor_the_security_baseline()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPublicAngebotClient>();
                services.AddSingleton<IPublicAngebotClient>(new ThrowingPublicAngebotClient());
            }));
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Token, html, StringComparison.Ordinal);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    private sealed class ThrowingPublicAngebotClient : IPublicAngebotClient
    {
        internal const string Message = "deliberate failure inside the API client";

        public Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<CustomerDecisionOutcome> RecordDecisionAsync(
            string token,
            CustomerDecisionChoice choice,
            string? reason,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);
    }
}
