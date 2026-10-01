using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// The marketing shell as a visitor receives it, with the site enabled against the fictional Alpha pack
/// (<b>D103</b>).
/// </summary>
public sealed class SiteLayoutTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private async Task<string> Impressum()
    {
        using var client = site.Client();
        using var response = await client.GetAsync("/impressum");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- Structure and accessibility -----------------------------------------

    [Fact]
    public async Task The_page_is_german_with_a_skip_link_first_and_exactly_one_h1()
    {
        var html = await Impressum();

        Assert.Contains("<html lang=\"de\">", html, StringComparison.Ordinal);
        var body = html[html.IndexOf("<body", StringComparison.Ordinal)..];
        var firstLink = body.IndexOf("<a ", StringComparison.Ordinal);
        Assert.True(firstLink >= 0);
        Assert.Equal(body.IndexOf("<a class=\"site-skip-link\" href=\"#inhalt\">", StringComparison.Ordinal), firstLink);
        Assert.Contains("<main id=\"inhalt\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Html.Count(html, "<h1"));
        Assert.Contains("<h1 class=\"site-page-title\">Impressum</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_script_no_inline_style_and_no_style_attribute_is_rendered()
    {
        var html = await Impressum();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_primary_navigation_lists_only_the_pages_that_exist()
    {
        var html = await Impressum();

        // The homepage (Slice 3, D104) and the services overview (Slice 4, D105); still no speculative navigation.
        Assert.Equal(new[] { "/", "/leistungen" }, SiteNavigation.Primary.Select(item => item.Href));
        Assert.Contains("aria-label=\"Hauptnavigation\"", html, StringComparison.Ordinal);
        Assert.Equal(SiteNavigation.Primary.Count, Html.Count(html, "class=\"site-nav-link\""));
    }

    /// <summary>Every navigation entry must answer 200 — vacuous in Slice 2, and pinned for the slices that add them.</summary>
    [Fact]
    public async Task Every_primary_navigation_link_answers_200()
    {
        using var client = site.Client();

        foreach (var item in SiteNavigation.Primary)
        {
            using var response = await client.GetAsync(item.Href);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ---- Company facts from the pack ------------------------------------------

    /// <summary>
    /// The header's brand treatment is the company name as text, and the call to action is the phone number.
    /// <para>
    /// <b>No mark is drawn in the brand zone (Slice 5h).</b> The zone is painted in the accent colour, so a logo
    /// drawn in the brand's own colours loses whichever parts match its surface — the dark parts on a dark band,
    /// the accent parts on this one. The configured reversed asset stays a dark-surface asset and appears in the
    /// footer; the positive one, which would lose its dark parts against either, appears nowhere on the page.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_header_carries_the_name_as_text_and_the_phone_with_no_mark_on_the_accent_zone()
    {
        var html = await Impressum();
        var header = html[html.IndexOf("<header", StringComparison.Ordinal)..html.IndexOf("</header>", StringComparison.Ordinal)];

        Assert.Contains($"<span class=\"site-brand-name\">{MarketingSiteFixture.CompanyName}</span>", header, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", header, StringComparison.Ordinal);
        Assert.DoesNotContain("/brand/logo.svg", html, StringComparison.Ordinal);
        // The reversed asset is a dark-surface asset: the footer draws it, the header does not.
        Assert.Contains("<img class=\"site-footer-logo\" src=\"/brand/logo-invers.svg\" alt=\"\"", html, StringComparison.Ordinal);
        // HtmlEncoder escapes "+" unconditionally; a browser decodes it back (see CompanyIdentityRenderingTests).
        Assert.Contains("href=\"tel:&#x2B;490001111111\"", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// The angled edge of the brand zone is drawn behind it, never by clipping the zone: a clip-path on an element
    /// containing a link cuts that link's focus ring off at the diagonal, which is a WCAG 2.4.7 failure that reads
    /// as a styling detail (Slice 5h).
    /// </summary>
    [Fact]
    public async Task The_brand_zones_angle_is_drawn_behind_it_and_never_clips_the_link()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var zone = css[css.IndexOf(".site-brand-zone {", StringComparison.Ordinal)..];

        Assert.DoesNotContain("clip-path", zone[..zone.IndexOf('}')], StringComparison.Ordinal);
        Assert.Matches("\\.site-brand-zone::after \\{[^}]*clip-path: polygon\\(", css);
        // The zone paints the accent itself, so the brand name's contrast is a real pairing rather than one that
        // depends on a pseudo-element having painted (measured 1.00:1 when it did not).
        Assert.Matches("\\.site-brand-zone \\{\\s*background: var\\(--color-accent\\);", css);
    }

    [Fact]
    public async Task The_footer_carries_address_contact_hours_area_and_legal_links()
    {
        var html = await Impressum();

        Assert.Contains("<address class=\"site-address\">", html, StringComparison.Ordinal);
        Assert.Contains("Teststraße 1", html, StringComparison.Ordinal);
        Assert.Contains("00000 Testort Alpha", html, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test\"", html, StringComparison.Ordinal);
        Assert.Contains("<abbr title=\"Montag bis Freitag\">Mo–Fr</abbr> 08:00–17:00 Uhr", html, StringComparison.Ordinal);
        Assert.Contains("Samstag nach Vereinbarung (Testdaten)", html, StringComparison.Ordinal);
        Assert.Contains("Testort Alpha und Testregion Nord", html, StringComparison.Ordinal);
        Assert.Contains("Weitere Orte nach Absprache (Testdaten).", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/impressum\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/datenschutz\"", html, StringComparison.Ordinal);
        // Every service page, from every page (Slice 4, D105).
        Assert.Contains("<h2 id=\"site-footer-services\" class=\"site-footer-heading\">Leistungen</h2>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/leistungen/test-leistung-eins\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/leistungen/test-leistung-zwei\"", html, StringComparison.Ordinal);
        Assert.Contains($"&copy; {DateTimeOffset.UtcNow.Year} {MarketingSiteFixture.CompanyName}", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Schnellkontakt\"", html, StringComparison.Ordinal);
    }

    /// <summary>Beta configures no hours and no service area: those blocks are absent, not empty.</summary>
    [Fact]
    public async Task Facts_that_are_not_configured_leave_no_trace()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://beta-musterwerkstatt.test") });

        using var response = await client.GetAsync("/unbekannt");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Beta Musterwerkstatt (Testdaten)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Öffnungszeiten", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Einsatzgebiet", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Rechtliches", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-brand-logo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha", html, StringComparison.Ordinal);
    }

    // ---- Head metadata ---------------------------------------------------------

    [Fact]
    public async Task Title_canonical_and_open_graph_follow_the_pattern()
    {
        var html = await Impressum();
        var canonical = $"{MarketingSiteFixture.CanonicalOrigin}/impressum";

        Assert.Contains($"<title>Impressum | {MarketingSiteFixture.CompanyName}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<link rel=\"canonical\" href=\"{canonical}\" />", html, StringComparison.Ordinal);
        Assert.Equal(canonical, Html.AttributeOf(html, "og:url", "content"));
        Assert.Equal(MarketingSiteFixture.CompanyName, Html.AttributeOf(html, "og:site_name", "content"));
        Assert.Equal("Impressum", Html.AttributeOf(html, "og:title", "content"));
        Assert.Equal("de_DE", Html.AttributeOf(html, "og:locale", "content"));
        Assert.Equal("website", Html.AttributeOf(html, "og:type", "content"));
        Assert.DoesNotContain("og:image", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"robots\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_canonical_url_ignores_the_query_string()
    {
        using var client = site.Client();

        var html = await client.GetStringAsync("/impressum?utm_source=test");

        Assert.Contains($"<link rel=\"canonical\" href=\"{MarketingSiteFixture.CanonicalOrigin}/impressum\" />", html, StringComparison.Ordinal);
    }

    // ---- Assets: same origin only ----------------------------------------------

    [Fact]
    public async Task The_font_is_preloaded_and_every_stylesheet_is_same_origin()
    {
        var html = await Impressum();

        Assert.Contains("<link rel=\"preload\" href=\"/fonts/figtree/figtree-latin-400-normal.woff2\" as=\"font\" type=\"font/woff2\" crossorigin", html, StringComparison.Ordinal);
        // MapStaticAssets fingerprints the file name itself.
        Assert.Matches("<link rel=\"stylesheet\" href=\"/css/marketing\\.[a-z0-9]+\\.css\" />", html);
        Assert.Matches("<link rel=\"stylesheet\" href=\"/site/theme.css\\?v=[0-9a-f]{12}\" />", html);
        Assert.DoesNotContain("https://fonts.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"//", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"http", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/fonts/figtree/figtree-latin-400-normal.woff2", 11384)]
    [InlineData("/fonts/figtree/figtree-latin-600-normal.woff2", 11544)]
    [InlineData("/fonts/figtree/figtree-latin-700-normal.woff2", 11376)]
    public async Task The_three_figtree_weights_are_served_as_woff2(string path, long size)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(size, (await response.Content.ReadAsByteArrayAsync()).LongLength);
    }

    [Fact]
    public async Task The_font_licence_ships_beside_the_font()
    {
        using var client = site.Client();

        var licence = await client.GetStringAsync("/fonts/figtree/OFL.txt");

        Assert.Contains("SIL Open Font License, Version 1.1", licence, StringComparison.Ordinal);
        Assert.Contains("Copyright 2022 The Figtree Project Authors", licence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stylesheet_declares_figtree_from_this_origin_only()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");

        Assert.Equal(3, Html.Count(css, "@font-face"));
        Assert.Contains("url(\"/fonts/figtree/figtree-latin-400-normal.woff2\")", css, StringComparison.Ordinal);
        Assert.Contains("font-display: swap", css, StringComparison.Ordinal);
        Assert.DoesNotContain("url(\"http", css, StringComparison.Ordinal);
        Assert.DoesNotContain("url(\"//", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Found by printing to PDF, not by a test</b>: in print the footer loses its background, and the footer
    /// links kept their white screen colour because <c>.site-footer a</c> lost to the more specific
    /// <c>.site-footer .site-footer-link</c>. Phone, email and legal links printed invisibly. Pinned here so the
    /// print rule keeps naming the specific selector.
    /// </summary>
    [Fact]
    public async Task The_print_stylesheet_turns_every_footer_link_black()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");
        var print = css[css.IndexOf("@media print", StringComparison.Ordinal)..];

        Assert.Contains(".site-footer .site-footer-link", print, StringComparison.Ordinal);
        Assert.Contains("color: #000000 !important;", print, StringComparison.Ordinal);
        Assert.Contains(".site-callbar", print, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_theme_stylesheet_carries_the_packs_colours()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/site/theme.css");
        var css = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--brand-primary: #1F4B7A;", css, StringComparison.Ordinal);
        Assert.Contains("--brand-accent: #E8C27A;", css, StringComparison.Ordinal);
        Assert.Contains("max-age=3600", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_theme_the_stylesheet_carries_the_product_defaults()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/site/theme.css");

        Assert.Contains("--brand-primary: #2B3A4A;", css, StringComparison.Ordinal);
        Assert.Contains("--brand-accent: #C8D3DE;", css, StringComparison.Ordinal);
    }
}
