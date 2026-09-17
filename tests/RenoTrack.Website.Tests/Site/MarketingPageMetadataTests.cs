using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenoTrack.Website.Content;
using RenoTrack.Website.Security;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// <b>Where marketing-page metadata exists, and that nothing else decides marketing behaviour</b>
/// (<b>D103</b>, Slice 2 decision S2-9).
/// </summary>
public sealed class MarketingPageMetadataTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private static IReadOnlyList<RouteEndpoint> Endpoints(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static int MarkersOn(RouteEndpoint endpoint) =>
        endpoint.Metadata.OfType<MarketingPageMetadata>().Count();

    private static RouteEndpoint Route(IReadOnlyList<RouteEndpoint> endpoints, string rawText) =>
        Assert.Single(endpoints, endpoint =>
            string.Equals(endpoint.RoutePattern.RawText?.Trim('/'), rawText.Trim('/'), StringComparison.OrdinalIgnoreCase));

    // ---- Enabled: exactly the marketing pages carry it -----------------------

    [Theory]
    [InlineData("/impressum")]
    [InlineData("/datenschutz")]
    [InlineData("/nicht-gefunden")]
    public void With_the_site_enabled_each_marketing_page_carries_exactly_one_marker(string route)
    {
        _ = site.Client();

        Assert.Equal(1, MarkersOn(Route(Endpoints(site.Factory.Services), route)));
    }

    [Fact]
    public void With_the_site_enabled_no_other_endpoint_carries_the_marker()
    {
        _ = site.Client();
        var endpoints = Endpoints(site.Factory.Services);

        var marked = endpoints.Where(endpoint => MarkersOn(endpoint) > 0)
            .Select(endpoint => endpoint.RoutePattern.RawText!.Trim('/'))
            .Order(StringComparer.Ordinal);

        // "" is the homepage at "/" (Slice 3, D104).
        Assert.Equal(new[] { "", "datenschutz", "impressum", "nicht-gefunden" }, marked);
        Assert.Equal(0, MarkersOn(Route(endpoints, "/angebot/{token}")));
        Assert.All(
            endpoints.Where(endpoint => endpoint.RoutePattern.Parameters.Any(parameter => parameter.Name == "token")),
            endpoint => Assert.Equal(0, MarkersOn(endpoint)));
    }

    // ---- Disabled: nothing carries it ----------------------------------------

    [Fact]
    public void With_the_site_disabled_no_endpoint_in_the_application_carries_the_marker()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        _ = factory.CreateClient();

        var endpoints = Endpoints(factory.Services);

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.Equal(0, MarkersOn(endpoint)));
    }

    // ---- Request time: the endpoint decides, configuration does not -----------

    /// <summary>
    /// <b>Proof that no marketing consumer re-reads configuration per request.</b> The host is composed with the
    /// site enabled; then the registered <see cref="SiteOptions"/> is swapped for a disabled one. Headers, layout
    /// and canonical-path redirects must still follow the endpoint metadata the startup convention added.
    /// </summary>
    [Fact]
    public async Task Marketing_behaviour_follows_the_endpoint_even_when_the_registered_options_say_disabled()
    {
        using var swapped = site.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<SiteOptions>();
            services.AddSingleton(new SiteOptions());
        }));
        using var client = swapped.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(MarketingSiteFixture.CanonicalOrigin),
        });

        Assert.False(swapped.Services.GetRequiredService<SiteOptions>().IsEnabled);

        using var page = await client.GetAsync("/impressum");
        var html = await page.Content.ReadAsStringAsync();
        using var redirect = await client.GetAsync("/Impressum");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(MarketingSecurityHeaders.ContentSecurityPolicy, page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("class=\"site-skip-link\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
    }

    // ---- The token page is untouched -----------------------------------------

    [Fact]
    public async Task The_token_page_keeps_its_own_layout_and_headers_with_the_site_enabled()
    {
        using var client = site.Client();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Permissions-Policy"));
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single(), StringComparison.Ordinal);
        Assert.Contains("customer-header", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-skip-link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/css/marketing.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mixed_case_token_url_is_never_path_canonicalised()
    {
        using var client = site.Client();
        const string mixedCase = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdEf";

        using var response = await client.GetAsync($"/angebot/{mixedCase}/");

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
    }
}
