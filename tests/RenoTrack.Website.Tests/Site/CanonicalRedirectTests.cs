using System.Net;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>One canonical address per page: host and path (<b>D103</b>).</summary>
public sealed class CanonicalRedirectTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    // ---- Alias derivation (unit) ---------------------------------------------

    [Theory]
    [InlineData("example.test", "www.example.test")]
    [InlineData("www.example.test", "example.test")]
    [InlineData("WWW.Example.TEST", "example.test")]
    [InlineData("sub.example.test", "www.sub.example.test")]
    [InlineData("localhost", null)]
    [InlineData("intranet", null)]
    [InlineData("www.intranet", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("[::1]", null)]
    public void The_alias_is_the_www_counterpart_when_one_is_meaningful(string host, string? expected)
    {
        Assert.Equal(expected, CanonicalRedirects.AliasHostFor(host));
    }

    [Theory]
    [InlineData("/impressum", null)]
    [InlineData("/", null)]
    [InlineData("/Impressum", "/impressum")]
    [InlineData("/impressum/", "/impressum")]
    [InlineData("/IMPRESSUM//", "/impressum")]
    [InlineData("//evil.example.test/Impressum", null)]
    [InlineData("/\\evil.example.test/Impressum", null)]
    public void The_canonical_path_is_lower_case_without_a_trailing_slash_and_never_protocol_relative(string path, string? expected)
    {
        Assert.Equal(expected, CanonicalRedirects.CanonicalPathFor(path));
    }

    // ---- Path, on the real host ----------------------------------------------

    [Theory]
    [InlineData("/Impressum", "/impressum")]
    [InlineData("/IMPRESSUM", "/impressum")]
    [InlineData("/impressum/", "/impressum")]
    [InlineData("/Impressum/?x=1&Y=Z", "/impressum?x=1&Y=Z")]
    [InlineData("/Datenschutz", "/datenschutz")]
    public async Task A_non_canonical_marketing_path_is_permanently_redirected_keeping_the_query(string path, string location)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_canonical_marketing_path_is_served_directly()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/impressum?x=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("//evil.example.test/Impressum")]
    [InlineData("/%2Fevil.example.test/Impressum")]
    public async Task A_protocol_relative_looking_path_is_never_redirected_to_another_host(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(new Uri(MarketingSiteFixture.CanonicalOrigin + path));

        Assert.False(
            response.Headers.Location is { } location && location.OriginalString.Contains("evil", StringComparison.Ordinal),
            $"Redirected to '{response.Headers.Location}'.");
    }

    [Fact]
    public async Task A_post_is_never_path_canonicalised()
    {
        using var client = site.Client();

        using var response = await client.PostAsync("/Impressum", new StringContent(string.Empty));

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }

    // ---- Host, on the real host ----------------------------------------------

    [Theory]
    [InlineData("/impressum", "/impressum")]
    [InlineData("/Impressum?x=1", "/Impressum?x=1")]
    [InlineData("/unbekannt/Pfad?q=%C3%A4", "/unbekannt/Pfad?q=%C3%A4")]
    public async Task The_alias_host_is_permanently_redirected_to_the_canonical_origin_unchanged(string path, string expected)
    {
        using var client = site.Client(MarketingSiteFixture.AliasOrigin);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(MarketingSiteFixture.CanonicalOrigin + expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_non_get_on_the_alias_host_keeps_its_method()
    {
        using var client = site.Client(MarketingSiteFixture.AliasOrigin);

        using var response = await client.PostAsync("/impressum", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal(MarketingSiteFixture.CanonicalOrigin + "/impressum", response.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// A token is case-sensitive: the host hop copies it byte for byte, and the redirect still carries the
    /// token route's own protections.
    /// </summary>
    [Fact]
    public async Task A_token_url_on_the_alias_host_keeps_its_token_exactly_and_its_protections()
    {
        const string mixedCase = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdEf";
        using var client = site.Client(MarketingSiteFixture.AliasOrigin);

        using var response = await client.GetAsync($"/angebot/{mixedCase}");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"{MarketingSiteFixture.CanonicalOrigin}/angebot/{mixedCase}", response.Headers.Location!.OriginalString);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://other.example.test")]
    [InlineData("https://localhost")]
    [InlineData("https://10.0.0.5")]
    public async Task No_other_host_is_redirected(string origin)
    {
        using var client = site.Client(origin);

        using var response = await client.GetAsync("/impressum");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Disabled: none of it ------------------------------------------------

    [Theory]
    [InlineData("https://www.alpha-testbetrieb.test", "/Impressum")]
    [InlineData("https://alpha-testbetrieb.test", "/impressum")]
    [InlineData("http://localhost", $"/angebot/{Token}/")]
    public async Task With_the_site_disabled_nothing_is_redirected(string origin, string path)
    {
        using var factory = new ContentPackFactory(packRoot: null, LegalSettings());
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(origin),
        });

        using var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
    }

    private static (string, string)[] LegalSettings() =>
        LegalContent.Document(LegalContent.Impressum).Select(pair => (pair.Key, pair.Value!)).ToArray();
}
