using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// The homepage at <c>/</c>, as a visitor receives it (<b>D104</b>). Fictional packs only (<b>D100</b>).
/// </summary>
public sealed partial class HomePageTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private const string AlphaMetaTitle = "Testleistungen in Testort Alpha (Testdaten)";
    private const string AlphaHeadline = "Erfundene Testleistungen für Testort Alpha (Testdaten)";
    private const string AlphaSubheadline = "Eine erfundene Einleitung für automatisierte Tests des Startseiten-Heros (Testdaten).";

    private async Task<string> Home()
    {
        using var client = site.Client();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<(HttpStatusCode Status, string Html)> HomeOf(string siteJson)
    {
        using var pack = TemporaryContentPack.With(siteJson);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://www.example.test"),
        });

        using var response = await client.GetAsync("/");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A complete, fictional enabled site whose <c>Site</c> section the test supplies.</summary>
    private static string Pack(string siteSection, string identityExtras = "") => $$"""
        {
          "CompanyIdentity": {
            "DisplayName": "Testfirma (Testdaten)",
            "ContactPhone": "+49 000 3333333",
            "ContactEmail": "kontakt@example.test",
            "Address": { "StreetAddress": "Teststraße 3", "PostalCode": "00003", "Locality": "Testort", "CountryCode": "DE" }{{identityExtras}}
          },
          "Site": {
            "PublicBaseUrl": "https://www.example.test",
            {{siteSection}}
          }
        }
        """;

    /// <summary>
    /// The services, plus the overview title an enabled site requires since Slice 4 (D105) — each service carries
    /// its own required title too.
    /// </summary>
    private static string Services(int count) =>
        "\"ServicesPage\": { \"MetaTitle\": \"Übersichtstitel (Testdaten)\" }, \"Services\": [" + string.Join(",", Enumerable.Range(1, count).Select(index =>
        $$"""{ "Slug": "leistung-{{index}}", "Name": "Testleistung {{index}}", "Summary": "Zusammenfassung {{index}} (Testdaten).", "Offerings": [ "Angebot {{index}}" ], "MetaTitle": "Leistungstitel {{index}} (Testdaten)" }"""))
        + "]";

    private static string Section(string html, string id)
    {
        var start = html.IndexOf($"aria-labelledby=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"section '{id}' is missing");
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    // ---- The page ------------------------------------------------------------

    [Fact]
    public async Task The_homepage_answers_200_in_the_site_layout()
    {
        var html = await Home();

        Assert.Contains("<html lang=\"de\">", html, StringComparison.Ordinal);
        Assert.Contains("<main id=\"inhalt\" class=\"site-main site-main-full\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"site-footer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_head_request_answers_200()
    {
        using var client = site.Client();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task There_is_exactly_one_h1_and_it_is_the_configured_headline()
    {
        var html = await Home();

        Assert.Equal(1, Html.Count(html, "<h1"));
        Assert.Contains($"<h1 id=\"home-title\" class=\"page-hero-title\">{AlphaHeadline}</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_headline_the_h1_is_the_company_name()
    {
        var (status, html) = await HomeOf(Pack($$"""{{Services(1)}}, "Home": { "MetaTitle": "Testtitel (Testdaten)" }"""));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, Html.Count(html, "<h1"));
        Assert.Contains("<h1 id=\"home-title\" class=\"page-hero-title\">Testfirma (Testdaten)</h1>", html, StringComparison.Ordinal);
    }

    // ---- Head ----------------------------------------------------------------

    /// <summary>[C1] The company-authored title, verbatim — never the company name, never a suffix.</summary>
    [Fact]
    public async Task The_document_title_is_the_meta_title_verbatim()
    {
        var html = await Home();

        Assert.Contains($"<title>{AlphaMetaTitle}</title>", html, StringComparison.Ordinal);
        Assert.Equal(AlphaMetaTitle, Html.AttributeOf(html, "og:title", "content"));
        Assert.DoesNotContain($"<title>{MarketingSiteFixture.CompanyName}</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($" | {MarketingSiteFixture.CompanyName}</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_other_marketing_pages_keep_the_page_and_company_title()
    {
        using var client = site.Client();

        var html = await client.GetStringAsync("/impressum");

        Assert.Contains($"<title>Impressum | {MarketingSiteFixture.CompanyName}</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(AlphaMetaTitle, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_subheadline_is_the_meta_description()
    {
        var html = await Home();

        Assert.Equal(AlphaSubheadline, Html.AttributeOf(html, "name=\"description\"", "content"));
        Assert.Equal(AlphaSubheadline, Html.AttributeOf(html, "og:description", "content"));
    }

    [Fact]
    public async Task Without_a_subheadline_no_description_is_rendered()
    {
        var (_, html) = await HomeOf(Pack($$"""{{Services(1)}}, "Home": { "MetaTitle": "Testtitel (Testdaten)" }"""));

        Assert.DoesNotContain("name=\"description\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("og:description", html, StringComparison.Ordinal);
        Assert.DoesNotContain("page-hero-lead", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_homepage_is_indexable_and_canonical_at_the_origin_root()
    {
        var html = await Home();

        Assert.Equal($"{MarketingSiteFixture.CanonicalOrigin}/", Html.AttributeOf(html, "rel=\"canonical\"", "href"));
        Assert.Equal($"{MarketingSiteFixture.CanonicalOrigin}/", Html.AttributeOf(html, "og:url", "content"));
        Assert.DoesNotContain("name=\"robots\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_string_is_neither_redirected_nor_reflected_nor_canonical()
    {
        const string Probe = "Qp7Zx2Lm9Wd4Kv";
        using var client = site.Client();

        using var response = await client.GetAsync($"/?utm_source={Probe}&x=%3Cscript%3E");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Probe, html, StringComparison.Ordinal);
        Assert.Equal($"{MarketingSiteFixture.CanonicalOrigin}/", Html.AttributeOf(html, "rel=\"canonical\"", "href"));
    }

    // ---- Hero and contact -----------------------------------------------------

    [Fact]
    public async Task The_hero_carries_the_service_area_and_both_contact_actions()
    {
        var hero = Section(await Home(), "home-title");

        Assert.Contains("Einsatzgebiet: Testort Alpha und Testregion Nord", hero, StringComparison.Ordinal);
        Assert.Contains("class=\"site-button page-button-light page-hero-call\" href=\"tel:&#x2B;490001111111\"", hero, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test\">E-Mail schreiben<span class=\"page-print-only\">: kontakt@alpha-testbetrieb.test</span></a>", hero, StringComparison.Ordinal);
    }

    /// <summary>
    /// A link cannot be followed on paper, so every contact action carries the information it stands for: the
    /// phone number is the call button's own text, and the email address travels with "E-Mail schreiben" for print.
    /// </summary>
    [Fact]
    public async Task Every_contact_action_carries_its_phone_number_or_address_as_text()
    {
        var html = await Home();

        foreach (var id in new[] { "home-title", "home-contact" })
        {
            var section = Section(html, id);
            // Razor encodes the '+' of the visible number as &#x2B; too, as CompanyIdentityRenderingTests documents.
            Assert.Matches("href=\"tel:&#x2B;490001111111\">\\s*<span class=\"site-visually-hidden\">Anrufen: </span>(\\+|&#x2B;)49 000 1111111\\s*</a>", section);
            Assert.Contains("<span class=\"page-print-only\">: kontakt@alpha-testbetrieb.test</span>", section, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>Tech Lead correction at pre-commit review:</b> print must expose the contact actions as text, never hide
    /// them in favour of the footer. The call bar alone stays hidden on paper.
    /// </summary>
    [Fact]
    public async Task The_print_stylesheet_prints_contact_actions_as_text_and_hides_only_the_call_bar()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");
        var screen = css[..css.IndexOf("@media", StringComparison.Ordinal)];
        var print = css[css.IndexOf("@media print", StringComparison.Ordinal)..];

        Assert.Contains(".page-print-only {\n    display: none;", screen.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotMatch("\\.page-actions\\s*\\{\\s*display:\\s*none", print);
        Assert.Contains(".page-actions {\n        display: block !important;", print.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains(".page-actions .page-hero-call {", print, StringComparison.Ordinal);
        Assert.Contains(".page-actions .site-visually-hidden {", print, StringComparison.Ordinal);
        Assert.Contains(".page-print-only {\n        display: inline;", print.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Matches("\\.site-callbar,[^{]*\\{\\s*display:\\s*none !important;", print);
    }

    [Fact]
    public async Task The_contact_section_carries_phone_email_hours_and_notes()
    {
        var contact = Section(await Home(), "home-contact");

        Assert.Contains("<h2 id=\"home-contact\" class=\"page-section-title\">Kontakt aufnehmen</h2>", contact, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:&#x2B;490001111111\"", contact, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test\"", contact, StringComparison.Ordinal);
        Assert.Contains("<abbr title=\"Montag bis Freitag\">Mo–Fr</abbr> 08:00–17:00 Uhr", contact, StringComparison.Ordinal);
        Assert.Contains("Samstag nach Vereinbarung (Testdaten)", contact, StringComparison.Ordinal);
        Assert.Contains("Weitere Orte nach Absprache (Testdaten).", contact, StringComparison.Ordinal);
    }

    // ---- Services --------------------------------------------------------------

    [Fact]
    public async Task Every_service_is_shown_in_pack_order_with_its_summary_but_not_its_offerings()
    {
        var services = Section(await Home(), "home-services");

        // Linked to its page since Slice 4 (D105); the heading and its one link carry only the service name.
        var first = services.IndexOf("<h3 class=\"page-card-title\"><a class=\"page-card-link\" href=\"/leistungen/test-leistung-eins\">Testleistung Eins</a></h3>", StringComparison.Ordinal);
        var second = services.IndexOf("<h3 class=\"page-card-title\"><a class=\"page-card-link\" href=\"/leistungen/test-leistung-zwei\">Testleistung Zwei</a></h3>", StringComparison.Ordinal);
        Assert.True(first >= 0 && second >= 0, "both services must be present");
        Assert.True(first < second);
        Assert.Contains("Eine erfundene Leistung für automatisierte Tests (Testdaten).", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Erstes erfundenes Angebot", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Drittes erfundenes Angebot", services, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task The_page_renders_however_many_services_the_pack_lists(int count)
    {
        var (status, html) = await HomeOf(Pack($$"""{{Services(count)}}, "Home": { "MetaTitle": "Testtitel (Testdaten)" }"""));

        Assert.Equal(HttpStatusCode.OK, status);
        var services = Section(html, "home-services");
        Assert.Equal(count, Html.Count(services, "<li class=\"page-card page-card-linked\">"));

        var positions = Enumerable.Range(1, count)
            .Select(index => services.IndexOf($"href=\"/leistungen/leistung-{index}\">Testleistung {index}</a></h3>", StringComparison.Ordinal))
            .ToList();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
    }

    // ---- Optional sections ---------------------------------------------------------

    [Fact]
    public async Task Advantages_and_process_render_in_order_when_supplied()
    {
        var html = await Home();

        var advantages = Section(html, "home-advantages");
        Assert.Equal(3, Html.Count(advantages, "<li class=\"page-card\">"));
        Assert.Contains("<h3 class=\"page-card-title\">Erster Testvorteil</h3>", advantages, StringComparison.Ordinal);

        var process = Section(html, "home-process");
        Assert.Contains("<ol class=\"home-steps\">", process, StringComparison.Ordinal);
        Assert.Equal(4, Html.Count(process, "<li class=\"home-step\">"));

        var order = new[] { "home-title", "home-services", "home-advantages", "home-process", "home-contact" }
            .Select(id => html.IndexOf($"aria-labelledby=\"{id}\"", StringComparison.Ordinal))
            .ToList();
        Assert.All(order, position => Assert.True(position >= 0));
        Assert.Equal(order.Order(), order);
    }

    /// <summary>Beta supplies only its title: every optional block is absent whole, heading included.</summary>
    [Fact]
    public async Task Absent_optional_content_leaves_no_section_heading_or_placeholder()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://beta-musterwerkstatt.test") });

        var html = await client.GetStringAsync("/");

        foreach (var absent in new[] { "home-advantages", "Ihre Vorteile", "home-process", "So läuft es ab", "Inhaber", "page-hero-lead", "Einsatzgebiet:", "Öffnungszeiten", "page-contact-note" })
        {
            Assert.DoesNotContain(absent, html, StringComparison.Ordinal);
        }

        Assert.Contains("<title>Beta-Testleistung in Testort Beta (Testdaten)</title>", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"home-contact\"", html, StringComparison.Ordinal);
    }

    // ---- [C2] The owner line -----------------------------------------------------------

    [Fact]
    public async Task The_owner_is_named_as_inhaber_and_never_as_a_contact_person()
    {
        var html = await Home();

        Assert.Contains("<p class=\"home-owner\">Inhaber: Testperson Alpha</p>", Section(html, "home-advantages"), StringComparison.Ordinal);
        Assert.DoesNotContain("Ansprechpartner", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_an_owner_name_there_is_no_owner_line()
    {
        var (_, html) = await HomeOf(Pack($$"""
            {{Services(1)}},
            "Home": {
              "MetaTitle": "Testtitel (Testdaten)",
              "Advantages": [ { "Title": "Vorteil A", "Text": "Text A (Testdaten)." }, { "Title": "Vorteil B", "Text": "Text B (Testdaten)." } ]
            }
            """));

        Assert.Contains("aria-labelledby=\"home-advantages\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Inhaber", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Ansprechpartner", html, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Links ---------------------------------------------------------------------

    /// <summary>
    /// No link to a page a later slice builds, and never to a customer token page. <c>/leistungen</c> left this list
    /// in Slice 4, which built it (D105); the link test below proves every link that remains answers 200.
    /// </summary>
    [Theory]
    [InlineData("href=\"/projekte")]
    [InlineData("href=\"/ueber-uns")]
    [InlineData("href=\"/faq")]
    [InlineData("href=\"/kontakt")]
    [InlineData("href=\"/angebot")]
    public async Task No_link_points_at_a_page_that_does_not_exist_yet(string link)
    {
        Assert.DoesNotContain(link, await Home(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_services_section_links_every_service_page_and_the_overview()
    {
        var services = Section(await Home(), "home-services");

        Assert.Contains("href=\"/leistungen/test-leistung-eins\"", services, StringComparison.Ordinal);
        Assert.Contains("href=\"/leistungen/test-leistung-zwei\"", services, StringComparison.Ordinal);
        Assert.Contains("<a class=\"page-more-link\" href=\"/leistungen\">Alle Leistungen ansehen</a>", services, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_same_origin_link_on_the_homepage_answers_200()
    {
        var html = await Home();
        using var client = site.Client();

        var targets = HrefPattern().Matches(html)
            .Select(match => match.Groups["href"].Value)
            .Where(href => href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("/", targets);
        foreach (var target in targets)
        {
            using var response = await client.GetAsync(target);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"'{target}' answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task The_brand_links_home_and_the_navigation_marks_the_current_page()
    {
        var html = await Home();

        Assert.Contains("<a class=\"site-brand\" href=\"/\">", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"site-nav-link\" href=\"/\" aria-current=\"page\">Startseite</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_navigation_does_not_mark_the_homepage_current_on_another_page()
    {
        using var client = site.Client();

        var html = await client.GetStringAsync("/impressum");

        Assert.Contains("<a class=\"site-nav-link\" href=\"/\">Startseite</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", html, StringComparison.Ordinal);
    }

    // ---- Security --------------------------------------------------------------------

    [Fact]
    public async Task The_homepage_carries_the_marketing_headers_and_no_script_or_inline_style()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("script-src 'none'", string.Join(";", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task No_url_on_the_homepage_leaves_the_origin()
    {
        var html = await Home();

        var offOrigin = UrlAttributePattern().Matches(html)
            .Select(match => match.Groups["url"].Value)
            .Where(url => url.Contains("//", StringComparison.Ordinal)
                && !url.StartsWith(MarketingSiteFixture.CanonicalOrigin + "/", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offOrigin);
    }

    /// <summary>Company text is rendered encoded — in the title, the headline and the description alike.</summary>
    [Fact]
    public async Task Markup_in_home_content_is_rendered_inert()
    {
        var (status, html) = await HomeOf(Pack($$"""
            {{Services(1)}},
            "Home": {
              "MetaTitle": "<script>alert(1)</script> Titel",
              "Headline": "<b>Fett</b> Überschrift",
              "Subheadline": "\"><img src=x> Einleitung"
            }
            """));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>Fett</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x>", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt; Titel</title>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Fett&lt;/b&gt; Überschrift", html, StringComparison.Ordinal);
    }

    // ---- Stylesheet rules found in browser QA ---------------------------------------------

    /// <summary>
    /// <b>Found in QA, not by a test:</b> at 400% zoom, Tab stopped on a contact button half behind the fixed call
    /// bar. Focus scrolled into view now clears the bar on narrow screens, and nothing is reserved on wide ones.
    /// </summary>
    [Fact]
    public async Task Focus_scrolled_into_view_clears_the_fixed_call_bar_on_narrow_screens()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");
        var wide = css[css.IndexOf("@media (min-width: 768px)", StringComparison.Ordinal)..];

        Assert.Contains("scroll-padding-bottom: calc(var(--callbar-height)", css[..css.IndexOf("@media", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("scroll-padding-bottom: 0;", wide[..wide.IndexOf("@media", 1, StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Found in QA, not by a test:</b> at 200% text and 375 px the first navigation entry squeezed the brand name
    /// into a column about 1,100 px tall. The header wraps, and the brand claims a real basis.
    /// </summary>
    [Fact]
    public async Task The_header_wraps_rather_than_squeezing_the_brand()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");
        var inner = css[css.IndexOf(".site-header-inner {", StringComparison.Ordinal)..];
        var brand = css[css.IndexOf(".site-brand {", StringComparison.Ordinal)..];

        Assert.Contains("flex-wrap: wrap;", inner[..inner.IndexOf('}')], StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 10rem;", brand[..brand.IndexOf('}')], StringComparison.Ordinal);
    }

    // ---- Routing ---------------------------------------------------------------------

    [Theory]
    [InlineData("/Index")]
    [InlineData("/index")]
    [InlineData("/startseite")]
    [InlineData("/Startseite")]
    public async Task The_homepage_has_exactly_one_address(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("page-hero", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_site_disabled_the_root_is_a_bare_404()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    // ---- Another company, same build ---------------------------------------------------

    [Fact]
    public async Task Another_pack_renders_none_of_the_first_packs_homepage_content()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://beta-musterwerkstatt.test") });

        var html = await client.GetStringAsync("/");

        foreach (var alpha in new[] { AlphaMetaTitle, AlphaHeadline, AlphaSubheadline, "Testvorteil", "Testschritt", "Testleistung Eins", MarketingSiteFixture.CompanyName })
        {
            Assert.DoesNotContain(alpha, html, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex("href=\"(?<href>[^\"]*)\"")]
    private static partial Regex HrefPattern();

    [GeneratedRegex("(?:href|src|content)=\"(?<url>[^\"]*)\"")]
    private static partial Regex UrlAttributePattern();
}
