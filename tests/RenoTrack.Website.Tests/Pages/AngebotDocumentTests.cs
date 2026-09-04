using System.Net;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The rendered document (Wireframe A3): what a customer actually reads.
/// </summary>
/// <remarks>
/// Server-rendered, so the HTML the customer receives is the HTML asserted here — there is no
/// script to run and nothing to hydrate. Runs on any OS: the API is stubbed at the
/// <c>IPublicAngebotClient</c> boundary and no database is involved.
/// </remarks>
public sealed class AngebotDocumentTests : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private readonly CustomerWebsiteFactory factory;

    public AngebotDocumentTests(CustomerWebsiteFactory factory)
    {
        this.factory = factory;
        factory.RequestedTokens.Clear();
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical());
    }

    private async Task<string> RenderAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ---- Content -----------------------------------------------------------

    [Fact]
    public async Task Every_section_and_line_reaches_the_page()
    {
        var html = await RenderAsync();

        Assert.Contains("Abriss", html, StringComparison.Ordinal);
        Assert.Contains("Baustelleneinrichtung", html, StringComparison.Ordinal);
        Assert.Contains("Wände abbrechen", html, StringComparison.Ordinal);
        Assert.Contains("Schutt entsorgen", html, StringComparison.Ordinal);
        Assert.Contains("Gerüst stellen", html, StringComparison.Ordinal);
        Assert.Contains("Nichttragend, inkl. Entsorgung", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every figure on screen is the server's figure (D78). The page sums nothing and re-derives
    /// nothing — BR-11's rounding belongs to the Domain.
    /// </summary>
    [Theory]
    [InlineData("25,00 €")]      // unit price
    [InlineData("250,00 €")]     // line total
    [InlineData("400,00 €")]     // second section's line and its subtotal
    [InlineData("1.250,00 €")]   // first section's Zwischensumme
    [InlineData("1.650,00 €")]   // Nettobetrag
    [InlineData("64,00 €")]      // 16% MwSt
    [InlineData("237,50 €")]     // 19% MwSt
    [InlineData("1.951,50 €")]   // Gesamtsumme
    public async Task Every_figure_is_rendered_as_the_server_sent_it(string amount)
    {
        Assert.Contains(amount, await RenderAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Zwischensumme")]
    [InlineData("Zusammenfassung")]
    [InlineData("Nettobetrag")]
    [InlineData("Gesamtsumme")]
    [InlineData("Beschreibung")]
    [InlineData("Menge")]
    [InlineData("Einheit")]
    [InlineData("Einzelpreis")]
    public async Task The_document_is_labelled_in_german(string label)
    {
        Assert.Contains(label, await RenderAsync(), StringComparison.Ordinal);
    }

    /// <summary>One line per rate the server sent — a document legitimately mixes rates (BR-6).</summary>
    [Fact]
    public async Task Each_vat_rate_gets_its_own_line()
    {
        var html = await RenderAsync();

        Assert.Contains("zzgl. 16% MwSt", html, StringComparison.Ordinal);
        Assert.Contains("zzgl. 19% MwSt", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Units_and_quantities_read_the_german_way()
    {
        var html = await RenderAsync();

        // m2 is the one code whose storage form is an ASCII compromise.
        Assert.Contains("m²", html, StringComparison.Ordinal);
        Assert.Contains("pauschal", html, StringComparison.Ordinal);
        Assert.Contains("Stk", html, StringComparison.Ordinal);

        // Q3: trailing zeros trimmed, German decimal separator. Whitespace is collapsed away first
        // so the assertion is about the value in its cell, not about Razor's indentation.
        var withoutWhitespace = new string([.. html.Where(c => !char.IsWhiteSpace(c))]);
        Assert.Contains(">2,5<", withoutWhitespace, StringComparison.Ordinal);
        Assert.DoesNotContain(">2,50<", withoutWhitespace, StringComparison.Ordinal);
        Assert.Contains(">10<", withoutWhitespace, StringComparison.Ordinal);
    }

    /// <summary>
    /// Position numbers are derived from order, which is only meaningful because the projection's
    /// item ordering is deterministic (the Application-side change in the same slice).
    /// </summary>
    [Fact]
    public async Task Positions_are_numbered_per_wireframe_a3()
    {
        var html = await RenderAsync();

        Assert.Contains("Pos. 1", html, StringComparison.Ordinal);
        Assert.Contains("Pos. 2", html, StringComparison.Ordinal);
        Assert.Contains("1.001", html, StringComparison.Ordinal);
        Assert.Contains("1.002", html, StringComparison.Ordinal);
        Assert.Contains("2.001", html, StringComparison.Ordinal);
    }

    /// <summary>Sections and lines appear in the order the server sent them, not any other.</summary>
    [Fact]
    public async Task Sections_and_lines_render_in_server_order()
    {
        var html = await RenderAsync();

        Assert.True(
            html.IndexOf("Abriss", StringComparison.Ordinal)
                < html.IndexOf("Baustelleneinrichtung", StringComparison.Ordinal),
            "Sections must render in the order the API returned them.");

        Assert.True(
            html.IndexOf("Wände abbrechen", StringComparison.Ordinal)
                < html.IndexOf("Schutt entsorgen", StringComparison.Ordinal),
            "Line items must render in the order the API returned them.");
    }

    // ---- Decision state (Q4) -----------------------------------------------

    /// <summary>A pending Angebot carries no status message — it is simply the document.</summary>
    [Fact]
    public async Task A_pending_angebot_shows_no_status_message()
    {
        var html = await RenderAsync();

        Assert.DoesNotContain("customer-status", html, StringComparison.Ordinal);
        Assert.DoesNotContain("angenommen", html, StringComparison.Ordinal);
        Assert.DoesNotContain("abgelehnt", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Q4: a decided Angebot must not read identically to a pending one. The customer is told what
    /// they did, and the document still follows — BR-4 keeps viewing open after a decision.
    /// </summary>
    [Fact]
    public async Task An_approved_angebot_says_so_and_still_shows_the_document()
    {
        factory.Result = CustomerAngebotResult.Available(
            CustomerAngebotBuilder.Typical(CustomerAngebotDecision.Approved));

        var html = await RenderAsync();

        Assert.Contains("angenommen", html, StringComparison.Ordinal);
        Assert.DoesNotContain("abgelehnt", html, StringComparison.Ordinal);
        Assert.Contains("Wände abbrechen", html, StringComparison.Ordinal);
        Assert.Contains("1.951,50 €", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_angebot_says_so_and_still_shows_the_document()
    {
        factory.Result = CustomerAngebotResult.Available(
            CustomerAngebotBuilder.Typical(CustomerAngebotDecision.Rejected));

        var html = await RenderAsync();

        Assert.Contains("abgelehnt", html, StringComparison.Ordinal);
        Assert.Contains("Wände abbrechen", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The internal vocabulary never reaches the customer: they are told what they did, not that
    /// the aggregate is in <c>CustomerApproved</c>.
    /// </summary>
    [Theory]
    [InlineData(CustomerAngebotDecision.Pending)]
    [InlineData(CustomerAngebotDecision.Approved)]
    [InlineData(CustomerAngebotDecision.Rejected)]
    public async Task Internal_status_vocabulary_never_reaches_the_page(CustomerAngebotDecision decision)
    {
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical(decision));

        var html = await RenderAsync();

        Assert.DoesNotContain("CustomerApproved", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerRejected", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AngebotStatus", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pending", html, StringComparison.Ordinal);
    }

    // ---- Safety ------------------------------------------------------------

    /// <summary>
    /// Free text is typed by an Inspector and rendered to an anonymous reader. Razor's
    /// <c>@</c>-expressions HTML-encode, and there is deliberately no <c>Html.Raw</c> anywhere on a
    /// customer page — asserted rather than assumed, because this is the first slice that renders
    /// that text at all.
    /// </summary>
    [Fact]
    public async Task Free_text_is_html_encoded_not_executed()
    {
        factory.Result = CustomerAngebotResult.Available(
            CustomerAngebotBuilder.WithItemText(
                "<script>alert('x')</script>",
                "<img src=x onerror=alert(1)>"));

        var html = await RenderAsync();

        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    /// <summary>The credential must not survive into a page that now carries far more markup.</summary>
    [Fact]
    public async Task The_token_is_never_rendered_into_the_populated_document()
    {
        Assert.DoesNotContain(Token, await RenderAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The public contract excludes internal ids, staff identities and catalogue traceability, and
    /// the page must not reintroduce any of them.
    /// </summary>
    [Theory]
    [InlineData("catalogItemId")]
    [InlineData("leadId")]
    [InlineData("inspectionId")]
    [InlineData("createdByInspectorId")]
    [InlineData("reviewedByAdminId")]
    [InlineData("sortOrder")]
    public async Task No_internal_field_name_appears_in_the_document(string field)
    {
        Assert.DoesNotContain(field, await RenderAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Still no script and still no disclosure of the API's origin (D97).</summary>
    [Fact]
    public async Task The_document_loads_no_script_and_names_no_api_origin()
    {
        var html = await RenderAsync();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.example.test", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Prices are the sensitive payload, so the no-store guarantee is re-checked here.</summary>
    [Fact]
    public async Task A_priced_document_is_still_never_stored_by_a_cache()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/angebot/{Token}");

        // The strongly-typed header, so the assertion does not depend on how HttpClient chooses to
        // split the directives into values.
        Assert.True(response.Headers.CacheControl?.NoStore, "A priced customer document must be no-store.");
    }

    // ---- Edge cases --------------------------------------------------------

    /// <summary>
    /// Only one section needs items for an Angebot to be submittable, so a section with none is
    /// reachable. The Inspector put it in the document, so it is rendered with a zero subtotal
    /// rather than suppressed.
    /// </summary>
    [Fact]
    public async Task A_section_with_no_lines_is_rendered_with_a_zero_subtotal()
    {
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.WithEmptySection());

        var html = await RenderAsync();

        Assert.Contains("Noch offen", html, StringComparison.Ordinal);
        Assert.Contains("Pos. 2", html, StringComparison.Ordinal);
        Assert.Contains("0,00 €", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single-rate document renders one VAT line, and a 0% rate is rendered like any other —
    /// suppressing a line the server sent would be the page editing the company's document.
    /// </summary>
    [Fact]
    public async Task A_zero_percent_rate_is_rendered_rather_than_suppressed()
    {
        factory.Result = CustomerAngebotResult.Available(new CustomerAngebot(
            "ANG-2026-00099",
            CustomerAngebotDecision.Pending,
            NetTotal: 100.00m,
            VatBreakdown: [new CustomerVatLine(0m, 0m)],
            GrossTotal: 100.00m,
            Sections:
            [
                new CustomerSection("Abriss", 100.00m,
                    [new CustomerItem("Beratung", null, 1m, "pauschal", 100.00m, 100.00m)]),
            ]));

        var html = await RenderAsync();

        Assert.Contains("zzgl. 0% MwSt", html, StringComparison.Ordinal);
    }

    /// <summary>An unrecognised unit is a legitimate custom one and reaches the page unchanged.</summary>
    [Fact]
    public async Task A_custom_unit_reaches_the_page_unchanged()
    {
        factory.Result = CustomerAngebotResult.Available(new CustomerAngebot(
            "ANG-2026-00100",
            CustomerAngebotDecision.Pending,
            NetTotal: 50.00m,
            VatBreakdown: [new CustomerVatLine(19m, 9.50m)],
            GrossTotal: 59.50m,
            Sections:
            [
                new CustomerSection("Material", 50.00m,
                    [new CustomerItem("Zement", null, 2m, "Sack", 25.00m, 50.00m)]),
            ]));

        Assert.Contains("Sack", await RenderAsync(), StringComparison.Ordinal);
    }
}
