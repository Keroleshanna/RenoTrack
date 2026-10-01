using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// The visual system Slice 5v introduced (<b>D107</b>): the fact panel that crosses the hero's edge, and the
/// alternating surfaces that give the page its rhythm.
/// </summary>
/// <remarks>
/// These are structural claims, not styling preferences. A page that repeats a surface between two adjacent
/// sections is the flat "white → beige → white" rhythm the re-alignment gate rejected, and it is exactly the kind
/// of thing that creeps back the next time a section is added.
/// </remarks>
public sealed partial class VisualSystemTests(MarketingSiteFixture site) : IClassFixture<MarketingSiteFixture>
{
    private async Task<string> Get(string path)
    {
        using var client = site.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- The fact panel ----------------------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData("/leistungen/test-leistung-eins")]
    public async Task The_fact_panel_states_the_company_facts_under_a_hidden_heading(string path)
    {
        var html = await Get(path);

        var panel = html[html.IndexOf("class=\"page-facts\"", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</section>", StringComparison.Ordinal)];

        // Hidden, because the contact band and the footer state the same facts in full: the panel must not read as
        // a second "Kontakt" section competing with the real one.
        Assert.Contains("<h2 id=\"site-facts\" class=\"site-visually-hidden\">Kontakt auf einen Blick</h2>", panel, StringComparison.Ordinal);
        Assert.Contains("<span class=\"page-fact-label\">Telefon</span>", panel, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:&#x2B;490001111111\"", panel, StringComparison.Ordinal);
        Assert.Contains("kontakt@alpha-testbetrieb.test", panel, StringComparison.Ordinal);
        Assert.Contains("<abbr title=\"Montag bis Freitag\">Mo–Fr</abbr> 08:00–17:00 Uhr", panel, StringComparison.Ordinal);
        Assert.Contains("Testort Alpha und Testregion Nord", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cell with nothing configured is omitted whole (<b>D100</b>): Beta configures no hours and no service area,
    /// so the panel shows two facts rather than two empty labels.
    /// </summary>
    [Fact]
    public async Task A_pack_without_hours_or_an_area_shows_a_shorter_panel()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://beta-musterwerkstatt.test"),
        });

        var html = await client.GetStringAsync("/");

        var panel = html[html.IndexOf("class=\"page-facts\"", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</section>", StringComparison.Ordinal)];

        Assert.Equal(2, Html.Count(panel, "class=\"page-fact-label\""));
        Assert.Contains("Telefon", panel, StringComparison.Ordinal);
        Assert.Contains("E-Mail", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Erreichbar", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Einsatzgebiet", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The overlap is a wide-screen composition device and lives entirely in the stylesheet: the markup is the same
    /// at every width, so a phone never scrolls past a decorative offset.
    /// </summary>
    [Fact]
    public async Task The_panel_overlaps_only_from_the_wide_breakpoint()
    {
        using var client = site.Client();

        var css = (await client.GetStringAsync("/css/marketing.css")).ReplaceLineEndings("\n");
        var narrow = css[..css.IndexOf("@media", StringComparison.Ordinal)];
        var wide = css[css.IndexOf("@media (min-width: 1024px)", StringComparison.Ordinal)..];

        Assert.DoesNotMatch(@"\.page-facts \{[^}]*margin-top", narrow);
        Assert.Matches(@"\.page-facts \{\s*position: relative;\s*margin-top: calc\(-1 \* var\(--space-8\)\);", wide);
    }

    // ---- Alternating surfaces ----------------------------------------------------------

    /// <summary>
    /// No two adjacent bands share a surface, and the page ends on the dark frame: hero, contact band and footer.
    /// The rule is what stops the page flattening into "white → beige → white → beige → footer" as sections are
    /// added in Slices 5b and 6.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/leistungen")]
    [InlineData("/leistungen/test-leistung-eins")]
    [InlineData("/leistungen/test-leistung-zwei")]
    public async Task No_two_adjacent_sections_share_a_surface(string path)
    {
        var html = await Get(path);
        var main = html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];

        // Consecutive prose sections are one band, not two: a service page's descriptive sections continue the same
        // reading surface and are separated by their headings, with the stylesheet collapsing the padding between
        // them. Everything else must alternate.
        var bands = BandPattern().Matches(main).Select(match => (Surface: Surface(match.Value), Prose: match.Value.Contains("page-section-text", StringComparison.Ordinal))).ToList();
        var surfaces = bands
            .Where((band, index) => index == 0 || !(band.Prose && bands[index - 1].Prose))
            .Select(band => band.Surface)
            .ToList();

        Assert.True(surfaces.Count >= 3, $"'{path}' should draw at least three bands, found {surfaces.Count}");
        for (var index = 1; index < surfaces.Count; index++)
        {
            Assert.True(
                surfaces[index] != surfaces[index - 1],
                $"'{path}': bands {index} and {index + 1} are both '{surfaces[index]}'");
        }
    }

    /// <summary>The surface a band draws, named by the class that decides it.</summary>
    private static string Surface(string tag)
    {
        if (tag.Contains("page-hero", StringComparison.Ordinal)) return "night";
        if (tag.Contains("page-facts", StringComparison.Ordinal)) return "paper";
        if (tag.Contains("page-contact", StringComparison.Ordinal)) return "navy";
        // The specific surfaces first: a band carrying both "surface-dark" and a colour class is that colour, and
        // classifying it as night hid a real violation — the process band and the contact band were both navy, and
        // this test passed. Found by measuring painted background colours in the browser, not by reading markup.
        if (tag.Contains("surface-navy", StringComparison.Ordinal)) return "navy";
        if (tag.Contains("surface-dark", StringComparison.Ordinal)) return "night";
        if (tag.Contains("surface-stone", StringComparison.Ordinal)) return "stone";
        if (tag.Contains("surface-sand", StringComparison.Ordinal) || tag.Contains("page-section-warm", StringComparison.Ordinal)) return "sand";
        if (tag.Contains("surface-paper", StringComparison.Ordinal)) return "paper";
        if (tag.Contains("page-breadcrumb-bar", StringComparison.Ordinal)) return "sand";
        return "unclassified";
    }

    /// <summary>
    /// Every full-width band: the elements that draw a surface of their own. The class name must end at the
    /// match — without that, "page-hero-text" and "page-hero-media" inside the hero counted as two more bands and
    /// the test reported the hero as adjacent to itself.
    /// </summary>
    [GeneratedRegex("<(section|div)[^>]*class=\"(page-hero|page-section|page-facts|page-breadcrumb-bar)( [^\"]*)?\"[^>]*>")]
    private static partial Regex BandPattern();
}
