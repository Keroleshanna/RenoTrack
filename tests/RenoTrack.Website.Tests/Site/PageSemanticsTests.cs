using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Shared partials must not weaken a page's structure (<b>D105</b>, Tech Lead guardrail 2): no duplicate ids, no
/// dangling <c>aria-labelledby</c>, no skipped heading level, no duplicated or unnamed landmark — on every marketing
/// page that renders them, with a full pack and with a minimal one.
/// </summary>
public sealed class PageSemanticsTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData("/leistungen/test-leistung-eins")]
    [InlineData("/leistungen/test-leistung-zwei")]
    [InlineData("/impressum")]
    public async Task Every_alpha_page_is_structurally_sound(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PageSemantics.AssertWellFormed(await response.Content.ReadAsStringAsync(), path);
    }

    [Fact]
    public async Task The_site_404_is_structurally_sound()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/leistungen/unbekannt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        PageSemantics.AssertWellFormed(await response.Content.ReadAsStringAsync(), "404");
    }

    /// <summary>Beta omits every optional block, so each page's outline is its shortest form.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData("/leistungen/beta-testleistung")]
    public async Task Every_minimal_beta_page_is_structurally_sound(string path)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://beta-musterwerkstatt.test"),
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PageSemantics.AssertWellFormed(await response.Content.ReadAsStringAsync(), path);
    }

    /// <summary>The approved outlines, pinned exactly — the service cards are h3 under the homepage's h2 and h2 on
    /// the overview, and "Weitere Leistungen" nests its cards one level below itself.</summary>
    [Theory]
    [InlineData("/", "1 2 3 3 2 3 3 3 2 3 3 3 3 2 3 2 2 2 2 2")]
    [InlineData("/leistungen", "1 2 2 2 3 2 2 2 2 2")]
    [InlineData("/leistungen/test-leistung-eins", "1 2 2 2 2 3 2 3 2 2 2 2 2")]
    [InlineData("/leistungen/test-leistung-zwei", "1 2 2 3 2 3 2 2 2 2 2")]
    public async Task Each_page_keeps_its_approved_heading_outline(string path, string outline)
    {
        using var client = site.Client();

        var html = await client.GetStringAsync(path);

        Assert.Equal(outline, string.Join(" ", PageSemantics.HeadingLevels(html)));
    }

    [Fact]
    public void The_checker_itself_catches_each_defect_it_claims_to()
    {
        const string Shell = "<header></header><main>{0}</main><footer></footer>";

        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1 id=\"a\">x</h1><h2 id=\"a\">y</h2>"), "duplicate id"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1>x</h1><section aria-labelledby=\"nirgends\"></section>"), "dangling label"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1>x</h1><h3>y</h3>"), "skipped level"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1>x</h1><h1>y</h1>"), "two h1"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1>x</h1><nav aria-label=\"A\"></nav><nav aria-label=\"A\"></nav>"), "same nav name"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed(string.Format(Shell, "<h1>x</h1><nav></nav>"), "unnamed nav"));
        Assert.ThrowsAny<Exception>(() => PageSemantics.AssertWellFormed("<header></header><main><h1>x</h1></main><main></main><footer></footer>", "two mains"));

        PageSemantics.AssertWellFormed(string.Format(Shell, "<h1 id=\"t\">x</h1><section aria-labelledby=\"t\"><h2>y</h2><h3>z</h3></section><h2>w</h2>"), "sound");
    }
}
