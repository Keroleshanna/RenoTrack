using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Website.Site;
using static RenoTrack.Website.Tests.Site.ServicePageFixtures;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Photos on the pages (<b>D106</b>): the split hero, the exact responsive markup, the all-or-nothing card rule, the
/// page's own <c>og:image</c>, and the text-only layouts that remain first-class when a photo is absent.
/// </summary>
public sealed partial class PageMediaTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private const string Version = "\\?v=[0-9a-f]{16}";

    private async Task<string> Get(string path)
    {
        using var client = site.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Picture(string html, string within)
    {
        var start = html.IndexOf(within, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{within}' is missing");
        var picture = html.IndexOf("<picture>", start, StringComparison.Ordinal);
        Assert.True(picture >= 0, "picture is missing");
        return html[picture..(html.IndexOf("</picture>", picture, StringComparison.Ordinal) + "</picture>".Length)];
    }

    /// <summary>The exact markup of one responsive photo, with every URL checked for its content hash.</summary>
    private static void AssertPictureMarkup(string picture, string id, string alt, string sizes, bool priority)
    {
        var webp = $"/medien/{id}-480\\.webp{Version} 480w, /medien/{id}-960\\.webp{Version} 960w, /medien/{id}-1600\\.webp{Version} 1600w";
        var jpeg = $"/medien/{id}-480\\.jpg{Version} 480w, /medien/{id}-960\\.jpg{Version} 960w, /medien/{id}-1600\\.jpg{Version} 1600w";
        var loading = priority ? "fetchpriority=\"high\" decoding=\"async\"" : "loading=\"lazy\" decoding=\"async\"";

        Assert.Matches(
            $"^<picture>\\s*<source type=\"image/webp\" srcset=\"{webp}\" sizes=\"{Regex.Escape(sizes)}\" />\\s*" +
            $"<img src=\"/medien/{id}-960\\.jpg{Version}\" srcset=\"{jpeg}\" sizes=\"{Regex.Escape(sizes)}\" " +
            $"width=\"1600\" height=\"1067\" alt=\"{Regex.Escape(alt)}\" {loading} />\\s*</picture>$",
            picture);
    }

    // ---- The homepage hero ------------------------------------------------------------------------

    [Fact]
    public async Task The_homepage_hero_is_split_with_the_text_first_and_the_photo_after_it()
    {
        var html = await Get("/");
        var hero = Section(html, "home-title");

        Assert.Contains("<section class=\"page-hero page-hero-split\" aria-labelledby=\"home-title\">", html, StringComparison.Ordinal);
        var text = hero.IndexOf("<div class=\"page-hero-text\">", StringComparison.Ordinal);
        var h1 = hero.IndexOf("<h1 id=\"home-title\"", StringComparison.Ordinal);
        var actions = hero.IndexOf("class=\"page-actions\"", StringComparison.Ordinal);
        var media = hero.IndexOf("<div class=\"page-hero-media\">", StringComparison.Ordinal);
        Assert.True(text >= 0 && h1 >= 0 && actions >= 0 && media >= 0, "hero parts are missing");
        Assert.True(text < h1 && h1 < actions && actions < media, "the photo must follow the text and the actions");

        // Text never sits on the photo: the headline is not inside the media column.
        Assert.DoesNotContain("<h1", hero[media..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hero_photo_is_a_priority_picture_with_both_formats_all_widths_and_its_dimensions()
    {
        var picture = Picture(await Get("/"), "class=\"page-hero-media\"");

        AssertPictureMarkup(picture, "testbild-startseite", "Testbild mit farbigen Streifen und Gitterlinien (Testdaten)", PictureSizes.Hero, priority: true);
        Assert.DoesNotContain("loading=", picture, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_homepage_declares_its_hero_photo_as_its_og_image()
    {
        var html = await Get("/");

        Assert.Matches($"<meta property=\"og:image\" content=\"https://www\\.alpha-testbetrieb\\.test/medien/testbild-startseite-og\\.jpg{Version}\" />", html);
        Assert.Contains("<meta property=\"og:image:type\" content=\"image/jpeg\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image:width\" content=\"1200\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image:height\" content=\"630\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:image:alt\" content=\"Testbild mit farbigen Streifen und Gitterlinien (Testdaten)\" />", html, StringComparison.Ordinal);
    }

    // ---- Service pages ------------------------------------------------------------------------------

    [Fact]
    public async Task A_photographed_service_has_the_split_hero_and_its_own_og_image()
    {
        var html = await Get(EinsPath);

        Assert.Contains("<section class=\"page-hero page-hero-split\" aria-labelledby=\"page-title\">", html, StringComparison.Ordinal);
        AssertPictureMarkup(Picture(html, "class=\"page-hero-media\""), "testbild-leistung-eins", "Zweites Testbild mit grünen und goldenen Streifen (Testdaten)", PictureSizes.Hero, priority: true);
        Assert.Matches($"og:image\" content=\"https://www\\.alpha-testbetrieb\\.test/medien/testbild-leistung-eins-og\\.jpg{Version}\"", html);
        Assert.Equal(1, Html.Count(html, "<picture>"));
    }

    /// <summary>S5-8: no fallback. A page without its own photo declares no og:image at all.</summary>
    [Theory]
    [InlineData(ZweiPath)]
    [InlineData("/leistungen")]
    [InlineData("/impressum")]
    public async Task A_page_without_its_own_photo_has_no_photo_and_no_og_image(string path)
    {
        var html = await Get(path);

        Assert.DoesNotContain("og:image", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<picture", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", MainContent(html), StringComparison.Ordinal);
        Assert.DoesNotContain("page-hero-split", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unphotographed_service_keeps_the_text_only_hero_with_no_placeholder()
    {
        var hero = Section(await Get(ZweiPath), "page-title");

        Assert.Contains("<h1 id=\"page-title\" class=\"page-hero-title\">Testleistung Zwei</h1>", hero, StringComparison.Ordinal);
        Assert.DoesNotContain("page-hero-media", hero, StringComparison.Ordinal);
        Assert.DoesNotContain("page-hero-grid", hero, StringComparison.Ordinal);
    }

    // ---- Cards: all or nothing (S5-7) ------------------------------------------------------------------

    /// <summary>The fixture's shape is the real one: one service photographed, one not — so no card shows a photo.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData(ZweiPath)]
    public async Task With_only_some_services_photographed_no_card_shows_a_photo(string path)
    {
        var cards = MainContent(await Get(path));

        Assert.DoesNotContain("page-card-with-image", cards, StringComparison.Ordinal);
        Assert.DoesNotContain("page-card-media", cards, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", 2, 3)]
    [InlineData("/leistungen", 2, 2)]
    [InlineData(EinsPath, 1, 3)]
    public async Task With_every_service_photographed_every_card_shows_a_lazy_photo(string path, int cards, int headingLevel)
    {
        using var pack = MediaPack.AllServicesPhotographed();
        using var client = pack.Client();

        var html = await client.GetStringAsync(path);

        Assert.Equal(cards, Html.Count(html, "<li class=\"page-card page-card-linked page-card-with-image\">"));
        Assert.DoesNotContain("<li class=\"page-card page-card-linked\">", html, StringComparison.Ordinal);

        var card = html[html.IndexOf("page-card-with-image", StringComparison.Ordinal)..];
        var picture = card[card.IndexOf("<picture>", StringComparison.Ordinal)..(card.IndexOf("</picture>", StringComparison.Ordinal) + "</picture>".Length)];
        Assert.Contains($"sizes=\"{PictureSizes.Card}\"", picture, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\" decoding=\"async\"", picture, StringComparison.Ordinal);
        Assert.DoesNotContain("fetchpriority", picture, StringComparison.Ordinal);

        // The photo precedes the heading and sits outside the one link, so the card keeps one accessible name.
        Assert.True(card.IndexOf("<picture>", StringComparison.Ordinal) < card.IndexOf($"<h{headingLevel} class=\"page-card-title\">", StringComparison.Ordinal));
        Assert.DoesNotMatch("<a [^>]*>[^<]*<picture", card);
    }

    [Fact]
    public async Task With_every_service_photographed_every_image_url_answers_200_with_an_image()
    {
        using var pack = MediaPack.AllServicesPhotographed();
        using var client = pack.Client();

        foreach (var path in new[] { "/", "/leistungen", EinsPath, ZweiPath })
        {
            var html = await client.GetStringAsync(path);
            var urls = ImageUrlPattern().Matches(html).Select(match => match.Groups["url"].Value).Distinct().ToList();
            Assert.NotEmpty(urls);

            foreach (var url in urls)
            {
                using var response = await client.GetAsync(url);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: '{url}' answered {(int)response.StatusCode}");
                Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
            }
        }
    }

    // ---- No photos at all ------------------------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData(EinsPath)]
    public async Task Without_any_photos_every_page_is_exactly_text_only(string path)
    {
        using var pack = MediaPack.NoPhotos();
        using var client = pack.Client();

        var html = await client.GetStringAsync(path);

        foreach (var absent in new[] { "<picture", "<img", "og:image", "page-hero-split", "page-hero-media", "/medien/" })
        {
            Assert.DoesNotContain(absent, MainContent(html) + html[..html.IndexOf("<body", StringComparison.Ordinal)], StringComparison.Ordinal);
        }

        Assert.Contains("<section class=\"page-hero\" aria-labelledby=", html, StringComparison.Ordinal);
    }

    // ---- Structure, encoding, stylesheet ----------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData(EinsPath)]
    [InlineData(ZweiPath)]
    public async Task Pages_with_photos_stay_structurally_sound(string path)
    {
        using var pack = MediaPack.AllServicesPhotographed();
        using var client = pack.Client();

        PageSemantics.AssertWellFormed(await client.GetStringAsync(path), path);
    }

    [Fact]
    public async Task Markup_in_alt_text_is_rendered_inert_in_the_img_and_the_og_image_alt()
    {
        using var pack = MediaPack.With(site => site["Media"]![0]!["Alt"] = "\"><script>alert(1)</script> Testbild");
        using var client = pack.Client();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alt=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt; Testbild\"", html, StringComparison.Ordinal);
        Assert.Contains("og:image:alt\" content=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt; Testbild\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_source_reference_ever_reaches_a_page()
    {
        foreach (var path in new[] { "/", "/leistungen", EinsPath, ZweiPath })
        {
            var html = await Get(path);
            Assert.DoesNotContain("test-register-", html, StringComparison.Ordinal);
            Assert.DoesNotContain("SourceRef", html, StringComparison.Ordinal);
        }
    }

    /// <summary>Content photography is always an img: no background image and no CSS cropping anywhere in the stylesheet.</summary>
    [Fact]
    public async Task The_stylesheet_has_no_background_images_and_no_css_cropping()
    {
        using var client = site.Client();

        var css = await client.GetStringAsync("/css/marketing.css");

        Assert.DoesNotContain("background-image", css, StringComparison.Ordinal);
        Assert.DoesNotMatch("background:[^;]*url\\(", css);
        Assert.DoesNotContain("aspect-ratio", css, StringComparison.Ordinal);

        // The one object-fit is the brand logo's (Slice 2): a vector mark scaled into its box, not a photo crop.
        var objectFit = Regex.Matches(css, "object-fit\\s*:").Select(match => match.Index).ToList();
        Assert.Single(objectFit);
        var rule = css[..objectFit[0]];
        Assert.EndsWith(".site-brand-logo", rule[..rule.LastIndexOf('{')].TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Found in visual QA, not by a test (Slice 5a):</b> the chevron for photographed cards lost to the general
    /// chevron rule — same specificity, later in the file — so the decoration was drawn on the photo, and the fix was
    /// a more specific selector.
    /// <para>
    /// Slice 5v removes the possibility instead of out-specifying it (<b>D107</b>): the card's footer line is an
    /// ordinary element at the end of the card, after the photo, the heading and the text, so no CSS ordering
    /// accident can put it back on the photograph. This test therefore pins the structure rather than the
    /// specificity — the weaker property (a selector's weight) replaced by the stronger one (document order).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_photographed_cards_footer_line_cannot_sit_on_the_photo()
    {
        using var pack = MediaPack.AllServicesPhotographed();
        using var photographed = pack.Client();

        var html = await photographed.GetStringAsync("/leistungen");

        // Every card: photo, then heading, then text, then the footer line — so within a card the decoration can
        // never precede the photograph, whatever the stylesheet says.
        var photos = Regex.Matches(html, "<picture>").Select(match => match.Index).ToList();
        var lines = Regex.Matches(html, "class=\"page-card-go\"").Select(match => match.Index).ToList();
        Assert.NotEmpty(photos);
        Assert.Equal(photos.Count, lines.Count);
        Assert.All(photos.Zip(lines), pair => Assert.True(pair.Second > pair.First, "the card's footer line follows its photo"));

        using var client = site.Client();
        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");

        // Nothing is positioned over the media any more: the card's decoration is in flow, not absolute.
        Assert.DoesNotContain(".page-card-linked::before", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".page-card-with-image::before", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hero_photo_is_not_printed_and_card_photos_print_bounded()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var print = css[css.IndexOf("@media print", StringComparison.Ordinal)..];

        Assert.Matches("\\.page-hero-media\\s*\\{\\s*display: none !important;", print);
        Assert.Matches("\\.page-card-media img\\s*\\{\\s*width: auto;\\s*max-height: 8cm;", print);
    }

    /// <summary>The picture sizes mirror the grid: the split hero's column ratio and the card grid's columns.</summary>
    [Fact]
    public async Task The_hero_grid_the_sizes_attribute_depends_on_is_in_the_stylesheet()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var wide = css[css.IndexOf("@media (min-width: 1024px)", StringComparison.Ordinal)..];

        // Re-measured for the layered hero (Slice 5v, D107): 5fr/7fr in the wide container, so the photo column is
        // the larger one and the sizes attribute has to say so.
        Assert.Matches("\\.page-hero-grid \\{\\s*grid-template-columns: minmax\\(0, 5fr\\) minmax\\(0, 7fr\\);\\s*gap: var\\(--space-8\\);", wide);
        Assert.StartsWith("(min-width: 1024px) min(42rem, 54vw)", PictureSizes.Hero, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_customer_token_page_has_no_photo_and_no_og_image()
    {
        using var client = site.Client();

        using var response = await client.GetAsync("/angebot/QaTokenProbeMedia7Kx2");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("og:image", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<picture", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/medien/", html, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex("(?:src|srcset|content)=\"(?<url>/medien/[^\" ]+)")]
    private static partial Regex ImageUrlPattern();
}
