using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using static RenoTrack.Website.Tests.Site.ServicePageFixtures;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// The services overview at <c>/leistungen</c>, as a visitor receives it (<b>D105</b>). Fictional packs only (<b>D100</b>).
/// </summary>
public sealed class ServicesOverviewPageTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private async Task<string> Overview()
    {
        using var client = site.Client();
        using var response = await client.GetAsync("/leistungen");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- The page ------------------------------------------------------------------

    [Fact]
    public async Task The_overview_answers_200_in_the_site_layout()
    {
        var html = await Overview();

        Assert.Contains("<body class=\"site\">", html, StringComparison.Ordinal);
        Assert.Matches("/css/marketing\\.[a-z0-9]+\\.css", html);
        Assert.Equal(1, Html.Count(html, "<h1"));
    }

    [Fact]
    public async Task A_head_request_answers_200()
    {
        using var client = site.Client();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/leistungen"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Head ----------------------------------------------------------------------

    [Fact]
    public async Task The_title_is_the_configured_meta_title_verbatim_and_the_intro_is_the_description()
    {
        var html = await Overview();

        Assert.Contains($"<title>{AlphaOverviewTitle}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{AlphaOverviewTitle}\" />", html, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{AlphaOverviewIntro}\" />", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{AlphaOverviewIntro}\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"| {MarketingSiteFixture.CompanyName}</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_overview_is_indexable_and_canonical()
    {
        var html = await Overview();

        Assert.Contains($"<link rel=\"canonical\" href=\"{MarketingSiteFixture.CanonicalOrigin}/leistungen\" />", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:url\" content=\"{MarketingSiteFixture.CanonicalOrigin}/leistungen\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"robots\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_string_is_neither_redirected_nor_reflected_nor_canonical()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/leistungen?probe=Qz7Wv4Kp");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Qz7Wv4Kp", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{MarketingSiteFixture.CanonicalOrigin}/leistungen\"", html, StringComparison.Ordinal);
    }

    // ---- Content -------------------------------------------------------------------

    [Fact]
    public async Task The_hero_carries_headline_intro_service_area_and_both_contact_actions()
    {
        var hero = Section(await Overview(), "page-title");

        Assert.Contains($"<h1 id=\"page-title\" class=\"page-hero-title\">{AlphaOverviewHeadline}</h1>", hero, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"page-hero-lead\">{AlphaOverviewIntro}</p>", hero, StringComparison.Ordinal);
        Assert.Contains("Einsatzgebiet: Testort Alpha und Testregion Nord", hero, StringComparison.Ordinal);
        Assert.Contains("class=\"site-button page-button-light page-hero-call\" href=\"tel:&#x2B;490001111111\"", hero, StringComparison.Ordinal);
        // No service is in context on the overview, so the email carries no subject.
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test\">E-Mail schreiben<span class=\"page-print-only\">: kontakt@alpha-testbetrieb.test</span></a>", hero, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_service_is_a_linked_h2_card_in_pack_order_with_its_summary_but_not_its_offerings()
    {
        var main = MainContent(await Overview());

        var first = main.IndexOf("<h2 class=\"page-card-title\"><a class=\"page-card-link\" href=\"/leistungen/test-leistung-eins\">Testleistung Eins</a></h2>", StringComparison.Ordinal);
        var second = main.IndexOf("<h2 class=\"page-card-title\"><a class=\"page-card-link\" href=\"/leistungen/test-leistung-zwei\">Testleistung Zwei</a></h2>", StringComparison.Ordinal);
        Assert.True(first >= 0 && second >= 0, "both services must be present");
        Assert.True(first < second);
        Assert.Equal(2, Html.Count(main, "<li class=\"page-card page-card-linked\">"));
        Assert.Contains("<p class=\"page-card-text\">Eine erfundene Leistung für automatisierte Tests (Testdaten).</p>", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Erstes erfundenes Angebot", main, StringComparison.Ordinal);
    }

    /// <summary>One link per card, named by the service alone — no repeated "Mehr erfahren".</summary>
    [Fact]
    public async Task Each_card_has_exactly_one_link()
    {
        var main = MainContent(await Overview());

        Assert.Equal(2, Html.Count(main, "class=\"page-card-link\""));
        Assert.DoesNotContain("Mehr erfahren", main, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weitere Leistungen", main, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_contact_section_follows_the_cards()
    {
        var html = await Overview();
        var contact = Section(html, "kontakt");

        Assert.Contains("<h2 id=\"kontakt\" class=\"page-section-title\">Kontakt aufnehmen</h2>", contact, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test\"", contact, StringComparison.Ordinal);
        Assert.Contains("<abbr title=\"Montag bis Freitag\">Mo–Fr</abbr> 08:00–17:00 Uhr", contact, StringComparison.Ordinal);

        var cards = html.IndexOf("class=\"page-cards\"", StringComparison.Ordinal);
        Assert.True(cards >= 0);
        Assert.True(cards < html.IndexOf("aria-labelledby=\"kontakt\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_breadcrumb_links_home_and_marks_the_overview_current()
    {
        var breadcrumb = Breadcrumb(await Overview());

        Assert.Contains("aria-label=\"Pfadnavigation\"", breadcrumb, StringComparison.Ordinal);
        Assert.Contains("<ol class=\"page-breadcrumb-list\">", breadcrumb, StringComparison.Ordinal);
        Assert.Contains("<a class=\"page-breadcrumb-link\" href=\"/\">Startseite</a>", breadcrumb, StringComparison.Ordinal);
        Assert.Contains("<span aria-current=\"page\">Leistungen</span>", breadcrumb, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/leistungen\"", breadcrumb, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_navigation_marks_the_overview_current()
    {
        var html = await Overview();

        Assert.Contains("<a class=\"site-nav-link\" href=\"/leistungen\" aria-current=\"page\">Leistungen</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"site-nav-link\" href=\"/\">Startseite</a>", html, StringComparison.Ordinal);
    }

    /// <summary>Beta supplies only the overview title: the h1 is the product label, and no intro or description exists.</summary>
    [Fact]
    public async Task Without_a_headline_or_intro_the_h1_is_the_label_and_nothing_is_placeholdered()
    {
        var (pack, factory, client) = Beta();
        using (pack)
        using (factory)
        using (client)
        {
            var html = await client.GetStringAsync("/leistungen");

            Assert.Contains("<h1 id=\"page-title\" class=\"page-hero-title\">Leistungen</h1>", html, StringComparison.Ordinal);
            Assert.Contains("<title>Leistungen der Beta Musterwerkstatt (Testdaten)</title>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("page-hero-lead", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"description\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Einsatzgebiet:", html, StringComparison.Ordinal);
        }
    }

    // ---- Links and addresses ----------------------------------------------------------

    [Fact]
    public async Task Every_same_origin_link_answers_200()
    {
        var html = await Overview();
        using var client = site.Client();

        foreach (var target in SameOriginLinks(html))
        {
            using var response = await client.GetAsync(target);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"'{target}' answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task No_url_leaves_the_origin()
    {
        Assert.Empty(OffOriginUrls(await Overview(), MarketingSiteFixture.CanonicalOrigin));
    }

    [Theory]
    [InlineData("/Leistungen", "/leistungen")]
    [InlineData("/leistungen/", "/leistungen")]
    [InlineData("/LEISTUNGEN/", "/leistungen")]
    public async Task A_non_canonical_address_redirects_permanently(string path, string location)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    /// <summary>No page-name route and no Index page: the overview has exactly one address.</summary>
    [Theory]
    [InlineData("/leistung")]
    [InlineData("/leistungen/index")]
    [InlineData("/index/leistungen")]
    public async Task The_overview_has_exactly_one_address(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("page-hero", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---- Security -------------------------------------------------------------------------

    [Fact]
    public async Task The_overview_carries_the_marketing_headers_and_no_script_style_form_or_cookie()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/leistungen");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("script-src 'none'", string.Join(";", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", MainContent(html), StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Markup_in_overview_and_service_content_is_rendered_inert()
    {
        var (status, html) = await Get(Pack("""
            "ServicesPage": {
              "MetaTitle": "<script>alert(1)</script> Übersicht",
              "Headline": "<b>Fett</b> Leistungen",
              "Intro": "\"><img src=x> Einleitung"
            },
            "Services": [
              { "Slug": "eins", "Name": "<i>Kursiv</i> Leistung", "Summary": "<script>alert(2)</script>", "Offerings": [ "A" ], "MetaTitle": "Eins" }
            ]
            """), "/leistungen");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>Fett</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>Kursiv</i>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x>", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt; Übersicht</title>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;i&gt;Kursiv&lt;/i&gt; Leistung", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_site_disabled_the_overview_is_a_bare_404()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/leistungen");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_pack_renders_none_of_the_first_packs_overview_content()
    {
        var (pack, factory, client) = Beta();
        using (pack)
        using (factory)
        using (client)
        {
            var html = await client.GetStringAsync("/leistungen");

            foreach (var alpha in new[] { AlphaOverviewTitle, AlphaOverviewHeadline, AlphaOverviewIntro, "Testleistung Eins", "test-leistung-zwei", MarketingSiteFixture.CompanyName })
            {
                Assert.DoesNotContain(alpha, html, StringComparison.Ordinal);
            }

            Assert.Contains("href=\"/leistungen/beta-testleistung\"", html, StringComparison.Ordinal);
        }
    }
}
