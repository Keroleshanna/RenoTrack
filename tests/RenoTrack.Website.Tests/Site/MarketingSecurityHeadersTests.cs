using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Website.Security;

namespace RenoTrack.Website.Tests.Site;

/// <summary>The CSP and Permissions-Policy of marketing pages, and nowhere else (<b>D103</b>).</summary>
public sealed class MarketingSecurityHeadersTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    [Fact]
    public void The_policy_is_exactly_the_approved_one()
    {
        Assert.Equal(
            "default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; " +
            "connect-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'",
            MarketingSecurityHeaders.ContentSecurityPolicy);
        Assert.Equal(
            "camera=(), microphone=(), geolocation=(), payment=(), usb=(), browsing-topics=()",
            MarketingSecurityHeaders.PermissionsPolicy);
    }

    [Theory]
    [InlineData("/impressum", HttpStatusCode.OK)]
    [InlineData("/unbekannt", HttpStatusCode.NotFound)]
    [InlineData("/nicht-gefunden", HttpStatusCode.NotFound)]
    [InlineData("/Impressum", HttpStatusCode.MovedPermanently)]
    public async Task A_marketing_response_carries_the_csp_the_permissions_policy_and_the_baseline(string path, HttpStatusCode status)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(MarketingSecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(MarketingSecurityHeaders.PermissionsPolicy, response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    /// <summary>An indexable marketing page carries no robots header — the addendum's crawlability rule.</summary>
    [Fact]
    public async Task An_indexable_marketing_page_sends_no_robots_header()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/impressum");

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Theory]
    [InlineData($"/angebot/{Token}")]
    [InlineData("/site/theme.css")]
    [InlineData("/css/marketing.css")]
    [InlineData("/brand/logo.svg")]
    public async Task A_response_that_is_not_a_marketing_page_carries_no_marketing_headers(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Permissions-Policy"));
    }

    [Fact]
    public async Task With_the_site_disabled_the_legal_page_keeps_the_customer_layout_and_no_marketing_headers()
    {
        var settings = LegalContent.Document(LegalContent.Impressum).Select(pair => (pair.Key, pair.Value!)).ToArray();
        using var factory = new ContentPackFactory(packRoot: null, settings);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/impressum");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Permissions-Policy"));
        Assert.Contains("<h1 class=\"customer-title\">Impressum</h1>", html, StringComparison.Ordinal);
        Assert.Contains("/css/customer.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-skip-link", html, StringComparison.Ordinal);
    }
}
