using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>The marketing site's 404 page (<b>D103</b>).</summary>
public sealed class SiteNotFoundTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/unbekannt")]
    [InlineData("/leistungen/fliesen")]
    [InlineData("/fonts/figtree/nicht-da.woff2")]
    public async Task An_unknown_address_answers_404_with_the_site_page_marked_noindex(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<h1 class=\"site-page-title\">Seite nicht gefunden</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"canonical\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("og:url", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_itself_is_never_a_200()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/nicht-gefunden");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Whatever was requested — including something shaped like a customer token — is never echoed back.</summary>
    [Theory]
    [InlineData("/angebot-kaputt/Zq8Vn3LwP0sK2dYb7RtE5mHc9XuJ4gFa1iOe6BlQ", "Zq8Vn3LwP0sK2dYb7RtE5mHc9XuJ4gFa1iOe6BlQ")]
    [InlineData("/suche?q=Reflektiert-Xy7", "Reflektiert-Xy7")]
    [InlineData("/%3Cb%3Eunbekannt%3C%2Fb%3E", "unbekannt")]
    public async Task The_requested_address_is_never_reflected(string path, string marker)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(marker, html, StringComparison.Ordinal);
    }

    /// <summary>Legal content that is not written still 404s — now with the site page as its body.</summary>
    [Fact]
    public async Task An_unwritten_legal_page_is_a_site_404()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/datenschutz");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Seite nicht gefunden", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only GET and HEAD are re-executed. Whatever routing answers a POST to an unknown address with (405 here,
    /// identical with the site disabled) is left exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_post_to_an_unknown_address_is_not_re_executed()
    {
        using var client = site.Client();
        using var disabled = new ContentPackFactory(packRoot: null);
        using var disabledClient = disabled.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostAsync("/unbekannt", new StringContent(string.Empty));
        using var baseline = await disabledClient.PostAsync("/unbekannt", new StringContent(string.Empty));

        Assert.Equal(baseline.StatusCode, response.StatusCode);
        Assert.DoesNotContain("Seite nicht gefunden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/unbekannt")]
    [InlineData("/nicht-gefunden")]
    public async Task With_the_site_disabled_unknown_addresses_stay_bare_404s(string path)
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
