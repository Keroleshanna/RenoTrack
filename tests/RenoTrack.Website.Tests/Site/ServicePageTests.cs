using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Website.Content;
using RenoTrack.Website.Site;
using static RenoTrack.Website.Tests.Site.ServicePageFixtures;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// One service's page at <c>/leistungen/{slug}</c>, as a visitor receives it (<b>D105</b>). Fictional packs only
/// (<b>D100</b>).
/// </summary>
public sealed class ServicePageTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private async Task<string> Page(string path = EinsPath)
    {
        using var client = site.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- The page ------------------------------------------------------------------

    [Theory]
    [InlineData(EinsPath)]
    [InlineData(ZweiPath)]
    public async Task Every_configured_service_answers_200_in_the_site_layout(string path)
    {
        var html = await Page(path);

        Assert.Contains("<body class=\"site\">", html, StringComparison.Ordinal);
        Assert.Equal(1, Html.Count(html, "<h1"));
    }

    [Fact]
    public async Task A_head_request_answers_200()
    {
        using var client = site.Client();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, EinsPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Head ----------------------------------------------------------------------

    [Fact]
    public async Task The_title_is_the_services_meta_title_verbatim_with_its_description()
    {
        var html = await Page();

        Assert.Contains($"<title>{EinsTitle}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{EinsTitle}\" />", html, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{EinsDescription}\" />", html, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{EinsDescription}\" />", html, StringComparison.Ordinal);
    }

    /// <summary>[S4-2] No fallback to "{Name} | {company}", and an absent description renders none.</summary>
    [Fact]
    public async Task A_service_without_optional_head_content_still_uses_its_own_title_and_invents_no_description()
    {
        var html = await Page(ZweiPath);

        Assert.Contains($"<title>{ZweiTitle}</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"Testleistung Zwei | {MarketingSiteFixture.CompanyName}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"description\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("og:description", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EinsPath)]
    [InlineData(ZweiPath)]
    public async Task Each_service_page_is_indexable_and_canonical_at_its_own_address(string path)
    {
        var html = await Page(path);

        Assert.Contains($"<link rel=\"canonical\" href=\"{MarketingSiteFixture.CanonicalOrigin}{path}\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"robots\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_string_is_neither_reflected_nor_canonical()
    {
        using var client = site.Client();

        using var response = await client.GetAsync(EinsPath + "?probe=Hn3Rt8Lx");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Hn3Rt8Lx", html, StringComparison.Ordinal);
        Assert.Contains($"<link rel=\"canonical\" href=\"{MarketingSiteFixture.CanonicalOrigin}{EinsPath}\" />", html, StringComparison.Ordinal);
    }

    // ---- Hero ----------------------------------------------------------------------

    [Fact]
    public async Task The_hero_carries_headline_summary_area_and_both_contact_actions_with_the_subject()
    {
        var hero = Section(await Page(), "page-title");

        Assert.Contains($"<h1 id=\"page-title\" class=\"page-hero-title\">{EinsHeadline}</h1>", hero, StringComparison.Ordinal);
        Assert.Contains("<p class=\"page-hero-lead\">Eine erfundene Leistung für automatisierte Tests (Testdaten).</p>", hero, StringComparison.Ordinal);
        Assert.Contains("Einsatzgebiet: Testort Alpha und Testregion Nord", hero, StringComparison.Ordinal);
        Assert.Contains("class=\"site-button page-button-light page-hero-call\" href=\"tel:&#x2B;490001111111\"", hero, StringComparison.Ordinal);
        Assert.Contains(
            "href=\"mailto:kontakt@alpha-testbetrieb.test?subject=Anfrage%3A%20Testleistung%20Eins\">E-Mail schreiben<span class=\"page-print-only\">: kontakt@alpha-testbetrieb.test</span></a>",
            hero,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_headline_the_h1_is_the_service_name()
    {
        var hero = Section(await Page(ZweiPath), "page-title");

        Assert.Contains("<h1 id=\"page-title\" class=\"page-hero-title\">Testleistung Zwei</h1>", hero, StringComparison.Ordinal);
        Assert.Contains("?subject=Anfrage%3A%20Testleistung%20Zwei\"", hero, StringComparison.Ordinal);
    }

    // ---- Scope and sections ------------------------------------------------------------

    [Fact]
    public async Task Every_offering_is_listed_in_pack_order_under_leistungsumfang()
    {
        var scope = Section(await Page(), "leistungsumfang");

        Assert.Contains("<h2 id=\"leistungsumfang\" class=\"page-section-title\">Leistungsumfang</h2>", scope, StringComparison.Ordinal);
        Assert.Contains("<ul class=\"page-checklist\">", scope, StringComparison.Ordinal);
        var first = scope.IndexOf("<li class=\"page-checklist-item\">Erstes erfundenes Angebot</li>", StringComparison.Ordinal);
        var second = scope.IndexOf("<li class=\"page-checklist-item\">Zweites erfundenes Angebot</li>", StringComparison.Ordinal);
        Assert.True(first >= 0 && second >= 0, "both offerings must be present");
        Assert.True(first < second);
        Assert.DoesNotContain("Drittes erfundenes Angebot", scope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sections_render_in_order_with_their_paragraphs()
    {
        var html = await Page();

        var first = Section(html, "abschnitt-1");
        Assert.Contains("<h2 id=\"abschnitt-1\" class=\"page-prose-title\">Erster erfundener Abschnitt</h2>", first, StringComparison.Ordinal);
        var one = first.IndexOf("<p class=\"page-prose-paragraph\">Ein erster erfundener Absatz (Testdaten).</p>", StringComparison.Ordinal);
        var two = first.IndexOf("<p class=\"page-prose-paragraph\">Ein zweiter erfundener Absatz (Testdaten).</p>", StringComparison.Ordinal);
        Assert.True(one >= 0 && two >= 0, "both paragraphs must be present");
        Assert.True(one < two);

        var second = Section(html, "abschnitt-2");
        Assert.Contains("Zweiter erfundener Abschnitt", second, StringComparison.Ordinal);
        Assert.Contains("Ein dritter erfundener Absatz (Testdaten).", second, StringComparison.Ordinal);
        Assert.DoesNotContain("abschnitt-3", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_service_without_sections_has_no_section_heading_or_placeholder()
    {
        var html = await Page(ZweiPath);

        Assert.DoesNotContain("abschnitt-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("page-prose", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", MainContent(html), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_blocks_appear_in_the_approved_order()
    {
        var html = await Page();

        var order = new[]
            {
                "class=\"page-breadcrumb\"",
                "aria-labelledby=\"page-title\"",
                "aria-labelledby=\"leistungsumfang\"",
                "aria-labelledby=\"abschnitt-1\"",
                "aria-labelledby=\"abschnitt-2\"",
                "aria-labelledby=\"kontakt\"",
                "aria-labelledby=\"weitere-leistungen\"",
            }
            .Select(marker => html.IndexOf(marker, StringComparison.Ordinal))
            .ToList();

        Assert.All(order, position => Assert.True(position >= 0));
        Assert.Equal(order.Order(), order);
    }

    // ---- Contact, other services, breadcrumb ----------------------------------------------

    [Fact]
    public async Task The_contact_section_carries_the_subject_hours_and_notes()
    {
        var contact = Section(await Page(), "kontakt");

        Assert.Contains("<h2 id=\"kontakt\" class=\"page-section-title\">Kontakt aufnehmen</h2>", contact, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:&#x2B;490001111111\"", contact, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:kontakt@alpha-testbetrieb.test?subject=Anfrage%3A%20Testleistung%20Eins\"", contact, StringComparison.Ordinal);
        Assert.Contains("<span class=\"page-print-only\">: kontakt@alpha-testbetrieb.test</span>", contact, StringComparison.Ordinal);
        Assert.Contains("<abbr title=\"Montag bis Freitag\">Mo–Fr</abbr> 08:00–17:00 Uhr", contact, StringComparison.Ordinal);
        Assert.Contains("Weitere Orte nach Absprache (Testdaten).", contact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EinsPath, "/leistungen/test-leistung-zwei", "Testleistung Zwei")]
    [InlineData(ZweiPath, "/leistungen/test-leistung-eins", "Testleistung Eins")]
    public async Task Other_services_links_every_other_service_but_not_the_current_one(string path, string otherHref, string otherName)
    {
        var others = Section(await Page(path), "weitere-leistungen");

        Assert.Contains("<h2 id=\"weitere-leistungen\" class=\"page-section-title\">Weitere Leistungen</h2>", others, StringComparison.Ordinal);
        Assert.Contains($"<h3 class=\"page-card-title\"><a class=\"page-card-link\" href=\"{otherHref}\">{otherName}</a></h3>", others, StringComparison.Ordinal);
        Assert.Equal(1, Html.Count(others, "class=\"page-card-link\""));
        Assert.DoesNotContain($"href=\"{path}\"", others, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_with_one_service_has_no_other_services_section()
    {
        var (pack, factory, client) = Beta();
        using (pack)
        using (factory)
        using (client)
        {
            var html = await client.GetStringAsync("/leistungen/beta-testleistung");

            Assert.DoesNotContain("Weitere Leistungen", html, StringComparison.Ordinal);
            Assert.DoesNotContain("weitere-leistungen", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_breadcrumb_links_home_and_the_overview_and_marks_the_service_current()
    {
        var breadcrumb = Breadcrumb(await Page());

        Assert.Contains("aria-label=\"Pfadnavigation\"", breadcrumb, StringComparison.Ordinal);
        var home = breadcrumb.IndexOf("<a class=\"page-breadcrumb-link\" href=\"/\">Startseite</a>", StringComparison.Ordinal);
        var overview = breadcrumb.IndexOf("<a class=\"page-breadcrumb-link\" href=\"/leistungen\">Leistungen</a>", StringComparison.Ordinal);
        var current = breadcrumb.IndexOf("<span aria-current=\"page\">Testleistung Eins</span>", StringComparison.Ordinal);
        Assert.True(home >= 0 && overview >= 0 && current >= 0, "every breadcrumb step must be present");
        Assert.True(home < overview && overview < current);
        Assert.DoesNotContain($"href=\"{EinsPath}\"", breadcrumb, StringComparison.Ordinal);
    }

    /// <summary>[S4-7] Exact match only: on a service page the breadcrumb, not the navigation, says where you are.</summary>
    [Fact]
    public async Task The_navigation_marks_nothing_current_on_a_service_page()
    {
        var html = await Page();
        var header = html[..html.IndexOf("</header>", StringComparison.Ordinal)];

        Assert.Contains("<a class=\"site-nav-link\" href=\"/leistungen\">Leistungen</a>", header, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_footer_links_every_service_page()
    {
        var html = await Page();
        var footer = html[html.IndexOf("<footer", StringComparison.Ordinal)..];

        Assert.Contains("<nav class=\"site-footer-block\" aria-labelledby=\"site-footer-services\">", footer, StringComparison.Ordinal);
        Assert.Contains("<a class=\"site-footer-link\" href=\"/leistungen/test-leistung-eins\">Testleistung Eins</a>", footer, StringComparison.Ordinal);
        Assert.Contains("<a class=\"site-footer-link\" href=\"/leistungen/test-leistung-zwei\">Testleistung Zwei</a>", footer, StringComparison.Ordinal);
    }

    // ---- Links and addresses ----------------------------------------------------------

    [Theory]
    [InlineData(EinsPath)]
    [InlineData(ZweiPath)]
    public async Task Every_same_origin_link_answers_200(string path)
    {
        var html = await Page(path);
        using var client = site.Client();

        var targets = SameOriginLinks(html);
        Assert.Contains("/leistungen", targets);
        foreach (var target in targets)
        {
            using var response = await client.GetAsync(target);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"'{target}' answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task No_url_leaves_the_origin()
    {
        Assert.Empty(OffOriginUrls(await Page(), MarketingSiteFixture.CanonicalOrigin));
    }

    [Theory]
    [InlineData("/leistungen/TEST-LEISTUNG-EINS", EinsPath)]
    [InlineData("/Leistungen/Test-Leistung-Eins", EinsPath)]
    [InlineData("/leistungen/test-leistung-eins/", EinsPath)]
    public async Task A_non_canonical_address_redirects_to_the_lower_case_path_before_any_lookup(string path, string location)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    /// <summary>No normalisation, transliteration, fuzzy match or fallback: anything but an exact slug is the site 404.</summary>
    [Theory]
    [InlineData("/leistungen/unbekannt")]
    [InlineData("/leistungen/test-leistung-ein")]
    [InlineData("/leistungen/test-leistung-eins-")]
    [InlineData("/leistungen/test_leistung_eins")]
    [InlineData("/leistungen/test-leistung-eins.html")]
    [InlineData("/leistungen/test-leistung-eins/extra")]
    [InlineData("/leistungen/t%C3%BCren")]
    [InlineData("/leistungen/test%2Dleistung%2Deins%20")]
    [InlineData("/leistung/test-leistung-eins")]
    public async Task Anything_but_an_exact_slug_is_the_site_404(string path)
    {
        using var client = site.Client();

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<h1 class=\"site-page-title\">Seite nicht gefunden</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain("page-hero", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EinsTitle, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_slug_is_never_echoed()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/leistungen/sondierung-p4xk9w");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("p4xk9w", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    /// <summary>The slug in a request reaches no log entry under the shipped logging configuration.</summary>
    [Fact]
    public async Task Neither_a_known_nor_an_unknown_slug_reaches_the_log()
    {
        var logs = new CapturingLoggerProvider();
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var baseFactory = new ContentPackFactory(pack.Root);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(MarketingSiteFixture.CanonicalOrigin),
        });

        using (await client.GetAsync("/leistungen/sondierung-m7qz2v")) { }
        using (await client.GetAsync("/Leistungen/Sondierung-M7QZ2V")) { }
        using (await client.GetAsync(EinsPath)) { }

        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.RecordedPartsContain("m7qz2v") || entry.RecordedPartsContain("M7QZ2V") || entry.ScopesContain("m7qz2v")
            || entry.RecordedPartsContain("test-leistung-eins") || entry.ScopesContain("test-leistung-eins"));
    }

    // ---- Security -------------------------------------------------------------------------

    [Fact]
    public async Task The_service_page_carries_the_marketing_headers_and_no_script_style_form_or_cookie()
    {
        using var client = site.Client();

        using var response = await client.GetAsync(EinsPath);
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
    public async Task Markup_in_every_service_field_is_rendered_inert()
    {
        var (status, html) = await Get(Pack("""
            "ServicesPage": { "MetaTitle": "Übersicht" },
            "Services": [
              {
                "Slug": "eins",
                "Name": "<i>Kursiv</i> Leistung",
                "Summary": "<script>alert(1)</script> Zusammenfassung",
                "Offerings": [ "<img src=x onerror=alert(2)> Angebot" ],
                "MetaTitle": "<script>alert(3)</script> Titel",
                "Headline": "<b>Fett</b> Überschrift",
                "MetaDescription": "\"><svg onload=alert(4)> Beschreibung",
                "Sections": [ { "Heading": "<u>Unterstrichen</u>", "Paragraphs": [ "<a href=\"https://evil.example.test\">Link</a>" ] } ]
              }
            ]
            """), "/leistungen/eins");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>Fett</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>Kursiv</i>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<u>Unterstrichen</u>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"https://evil.example.test\"", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;script&gt;alert(3)&lt;/script&gt; Titel</title>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;a href=&quot;https://evil.example.test&quot;&gt;Link&lt;/a&gt;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A service name is company text and may contain any printable character. Percent-encoding the subject whole
    /// means it can never close the subject and open a <c>body</c>, <c>cc</c> or fragment of its own.
    /// </summary>
    [Fact]
    public async Task A_service_name_cannot_add_fields_to_the_mailto_link()
    {
        var (status, html) = await Get(Pack("""
            "ServicesPage": { "MetaTitle": "Übersicht" },
            "Services": [
              { "Slug": "eins", "Name": "Bad & Co?body=x#cc=%", "Summary": "S.", "Offerings": [ "A" ], "MetaTitle": "Eins" }
            ]
            """), "/leistungen/eins");

        Assert.Equal(HttpStatusCode.OK, status);

        // The name is also visible text (h1, breadcrumb), rendered encoded, so the check is on the link targets only.
        var mailtos = System.Text.RegularExpressions.Regex.Matches(html, "href=\"(?<href>mailto:[^\"]*)\"")
            .Select(match => match.Groups["href"].Value)
            .ToList();
        Assert.Equal(2, mailtos.Count(href => href.Contains("?subject=", StringComparison.Ordinal)));
        Assert.All(
            mailtos.Where(href => href.Contains("?subject=", StringComparison.Ordinal)),
            href => Assert.Equal("mailto:kontakt@example.test?subject=Anfrage%3A%20Bad%20%26%20Co%3Fbody%3Dx%23cc%3D%25", href));
        Assert.All(mailtos, href =>
        {
            Assert.Equal(href.IndexOf('?', StringComparison.Ordinal), href.LastIndexOf('?'));
            Assert.DoesNotContain("&", href, StringComparison.Ordinal);
            Assert.DoesNotContain("#", href, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task With_the_site_disabled_a_service_address_is_a_bare_404()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/leistungen/test-leistung-eins");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_pack_serves_none_of_the_first_packs_services()
    {
        var (pack, factory, client) = Beta();
        using (pack)
        using (factory)
        using (client)
        {
            using var missing = await client.GetAsync(EinsPath);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            var html = await client.GetStringAsync("/leistungen/beta-testleistung");
            foreach (var alpha in new[] { EinsTitle, EinsHeadline, EinsDescription, "Testleistung Eins", "Erstes erfundenes Angebot", "Erster erfundener Abschnitt", MarketingSiteFixture.CompanyName })
            {
                Assert.DoesNotContain(alpha, html, StringComparison.Ordinal);
            }

            Assert.Contains("<title>Beta-Testleistung bei der Beta Musterwerkstatt (Testdaten)</title>", html, StringComparison.Ordinal);
        }
    }

    // ---- Stylesheet --------------------------------------------------------------------------

    /// <summary>
    /// On paper the service pages' links are text: breadcrumb and card links print black, the drawn chevrons are
    /// dropped, and the contact actions reuse the homepage's print rules through the shared class names.
    /// </summary>
    [Fact]
    public async Task The_print_stylesheet_covers_the_service_page_components()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var print = css[css.IndexOf("@media print", StringComparison.Ordinal)..];

        Assert.Matches("\\.site \\.page-breadcrumb-link,\\s*\\.site a\\.page-card-link,\\s*\\.site a\\.page-more-link,", print);
        // Drawn decorations are screen affordances and cost ink: the hero's offset frame, the section rule, the card
        // index rule, the card's arrow and the "more" arrow (Slice 5v, D107).
        Assert.Matches("\\.page-card-go::after,\\s*\\.page-more-link::after \\{\\s*display: none !important;", print);
        Assert.Matches("\\.page-breadcrumb-bar\\s*\\{\\s*background: none;", print);
        Assert.Contains(".page-actions {\n        display: block !important;", print, StringComparison.Ordinal);
        Assert.Contains(".page-print-only {\n        display: inline;", print, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Found in QA, not by a test:</b> at 200% text and 375 px, "Leistungsumfang" as an inline-block section heading
    /// was wider than the viewport. A long single word must be able to break, independent of any hyphenation dictionary.
    /// </summary>
    [Fact]
    public async Task A_section_heading_can_break_a_long_word_instead_of_widening_the_page()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var rule = css[css.IndexOf(".page-section-title {", StringComparison.Ordinal)..];
        rule = rule[..rule.IndexOf('}')];

        Assert.Contains("overflow-wrap: anywhere;", rule, StringComparison.Ordinal);
        // Slice 5v measures the heading in characters rather than percent, so a long line wraps before it runs out
        // of measure; a single long word still breaks, which is what the QA defect was about.
        Assert.Contains("max-width: 20ch;", rule, StringComparison.Ordinal);
        Assert.Contains("hyphens: auto;", css[css.IndexOf(".page-section-title,", StringComparison.Ordinal)..][..200], StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Found in QA, not by a test:</b> with long service names in the footer's services column, 200% text at 375 px
    /// overflowed the page. The one-column footer track must be able to shrink, and a footer link must be able to break.
    /// </summary>
    [Fact]
    public async Task A_long_service_name_in_the_footer_cannot_widen_the_page()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var screen = css[..css.IndexOf("@media", StringComparison.Ordinal)];

        Assert.Matches("\\.site-footer-grid \\{\\s*display: grid;\\s*grid-template-columns: minmax\\(0, 1fr\\);", screen);
        var links = screen[screen.IndexOf(".site-footer-links a {", StringComparison.Ordinal)..];
        Assert.Contains("overflow-wrap: anywhere;", links[..links.IndexOf('}')], StringComparison.Ordinal);
    }

    /// <summary>The whole card is the click target, but the link keeps a visible focus indicator and the card shows it.</summary>
    [Fact]
    public async Task A_linked_card_is_one_target_with_visible_focus()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var screen = css[..css.IndexOf("@media", StringComparison.Ordinal)];

        Assert.Matches("\\.page-card-link::after\\s*\\{\\s*content: \"\";\\s*position: absolute;\\s*inset: 0;", screen);
        Assert.Matches("\\.page-card-linked:has\\(\\.page-card-link:focus-visible\\)\\s*\\{\\s*outline: 3px solid", screen);
        Assert.DoesNotMatch("\\.page-card-link:focus-visible\\s*\\{[^}]*outline:\\s*none", css);
    }

    // ---- The lookup itself (guardrail 1) ------------------------------------------------------

    private static MarketingSite Snapshot(params string[] slugs) => new(new SiteOptions
    {
        PublicBaseUrl = "https://www.example.test",
        Services = slugs.Select(slug => new ServiceOptions { Slug = slug, Name = slug, Summary = "S", Offerings = ["A"], MetaTitle = slug }).ToList(),
    });

    [Fact]
    public void The_lookup_returns_the_service_with_exactly_that_slug()
    {
        var marketing = Snapshot("innenausbau", "waende");

        Assert.Same(marketing.Services[1], marketing.FindService("waende"));
        Assert.Same(marketing.Services[0], marketing.FindService("innenausbau"));
    }

    /// <summary>
    /// Host-level tests cannot see this: the canonical-path redirect lower-cases every address before the page runs.
    /// So the ordinal, case-sensitive contract is proven on the lookup directly.
    /// </summary>
    [Theory]
    [InlineData("WAENDE")]
    [InlineData("Waende")]
    [InlineData("wände")]
    [InlineData("wäende")]
    [InlineData(" waende")]
    [InlineData("waende ")]
    [InlineData("waend")]
    [InlineData("waende-")]
    [InlineData("")]
    [InlineData(null)]
    public void The_lookup_never_normalises_transliterates_or_falls_back(string? slug)
    {
        Assert.Null(Snapshot("innenausbau", "waende").FindService(slug));
    }

    /// <summary>The Turkish dotted/dotless i is where a culture-sensitive comparison diverges from an ordinal one.</summary>
    [Fact]
    public void The_lookup_is_culture_independent()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var marketing = Snapshot("innenausbau");

            Assert.NotNull(marketing.FindService("innenausbau"));
            Assert.Null(marketing.FindService("ınnenausbau"));
            Assert.Null(marketing.FindService("INNENAUSBAU"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_service_path_is_the_overview_path_and_the_slug()
    {
        Assert.Equal("/leistungen/waende", MarketingSite.ServicePath(new ServiceOptions { Slug = "waende" }));
    }
}
