using System.Net;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The customer's invoice page as it is actually served (Phase 14 Slice 3b, D114): the route the
/// emailed link points at, what each status and each failure puts on screen with which HTTP status,
/// the headers a page whose URL is a credential must carry, and where the token may and may not
/// appear.
/// </summary>
public sealed class InvoicePageTests : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "Inv7-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";
    private const string PageUrl = $"/invoice/{Token}";
    private const string PdfHref = $"/invoice/{Token}/pdf";

    private readonly CustomerWebsiteFactory factory;

    public InvoicePageTests(CustomerWebsiteFactory factory)
    {
        this.factory = factory;
        factory.RequestedInvoiceTokens.Clear();
        factory.InvoiceResult = CustomerInvoiceResult.Available(CustomerInvoiceBuilder.Open());
    }

    private async Task<(HttpResponseMessage Response, string Html)> GetAsync(CustomerInvoiceResult result)
    {
        factory.InvoiceResult = result;
        using var client = factory.CreateClient();
        var response = await client.GetAsync(PageUrl);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static CustomerInvoiceResult Available(CustomerInvoiceStatus status, bool documentAvailable = true) =>
        CustomerInvoiceResult.Available(CustomerInvoiceBuilder.With(status, documentAvailable));

    // ---- The route -------------------------------------------------------------

    /// <summary>
    /// The path <c>EmailMessageFactory.InvoiceUrl</c> has composed since Phase 8 (D4.1). Until this
    /// slice it resolved to nothing.
    /// </summary>
    [Fact]
    public async Task The_emailed_route_resolves_and_the_token_reaches_the_api_untouched()
    {
        var (response, _) = await GetAsync(Available(CustomerInvoiceStatus.Open));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Token, Assert.Single(factory.RequestedInvoiceTokens));
    }

    [Fact]
    public async Task The_route_without_a_token_is_not_the_invoice_page()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/invoice");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.RequestedInvoiceTokens);
    }

    // ---- Open ------------------------------------------------------------------

    [Fact]
    public async Task An_open_invoice_shows_its_amounts_its_due_date_and_the_pdf_link()
    {
        var (response, html) = await GetAsync(Available(CustomerInvoiceStatus.Open));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("customer-document", html, StringComparison.Ordinal);
        Assert.Contains(CustomerInvoiceBuilder.Number, html, StringComparison.Ordinal);
        Assert.Contains($"Für {CustomerInvoiceBuilder.CustomerName}", html, StringComparison.Ordinal);
        Assert.Contains("6.722,69 €", html, StringComparison.Ordinal);
        Assert.Contains("1.277,31 €", html, StringComparison.Ordinal);
        Assert.Contains("8.000,00 €", html, StringComparison.Ordinal);
        Assert.Contains("Fällig am", html, StringComparison.Ordinal);
        Assert.Contains("15.09.2026", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{PdfHref}\"", html, StringComparison.Ordinal);
        Assert.Contains("Rechnung als PDF herunterladen", html, StringComparison.Ordinal);

        Assert.DoesNotContain("customer-status", html, StringComparison.Ordinal);
    }

    // ---- Paid ------------------------------------------------------------------

    [Fact]
    public async Task A_paid_invoice_says_so_shows_its_amounts_and_no_due_date()
    {
        var (response, html) = await GetAsync(Available(CustomerInvoiceStatus.Paid));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Diese Rechnung ist bezahlt.", html, StringComparison.Ordinal);
        Assert.Contains("8.000,00 €", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Fällig am", html, StringComparison.Ordinal);
        Assert.DoesNotContain("15.09.2026", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{PdfHref}\"", html, StringComparison.Ordinal);
    }

    // ---- Void ------------------------------------------------------------------

    /// <summary>
    /// The Product Owner's decision: cancelled is said plainly, nothing is due, and the archived PDF
    /// stays downloadable. The page explains beside the link that the PDF shows the invoice as issued.
    /// </summary>
    [Fact]
    public async Task A_voided_invoice_is_shown_as_cancelled_with_no_due_date_and_its_pdf_still_offered()
    {
        var (response, html) = await GetAsync(Available(CustomerInvoiceStatus.Void));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Diese Rechnung wurde storniert und ist nicht mehr zu zahlen.", html, StringComparison.Ordinal);
        Assert.Contains("8.000,00 €", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Fällig am", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{PdfHref}\"", html, StringComparison.Ordinal);
        Assert.Contains("Das PDF zeigt die Rechnung so, wie sie ausgestellt wurde.", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CustomerInvoiceStatus.Open)]
    [InlineData(CustomerInvoiceStatus.Paid)]
    public async Task Only_a_voided_invoice_carries_the_as_issued_note(CustomerInvoiceStatus status)
    {
        var (_, html) = await GetAsync(Available(status));

        Assert.DoesNotContain("so, wie sie ausgestellt wurde", html, StringComparison.Ordinal);
        Assert.DoesNotContain("storniert", html, StringComparison.Ordinal);
    }

    // ---- No document -----------------------------------------------------------------

    [Theory]
    [InlineData(CustomerInvoiceStatus.Open)]
    [InlineData(CustomerInvoiceStatus.Paid)]
    [InlineData(CustomerInvoiceStatus.Void)]
    public async Task Without_an_archived_document_no_pdf_link_is_offered(CustomerInvoiceStatus status)
    {
        var (response, html) = await GetAsync(Available(status, documentAvailable: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ein PDF dieser Rechnung ist hier nicht verfügbar.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/pdf", html, StringComparison.Ordinal);
        Assert.DoesNotContain("herunterladen", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, html, StringComparison.Ordinal);
    }

    // ---- Failure states ------------------------------------------------------------

    [Theory]
    [InlineData(CustomerInvoiceOutcome.NotFound, HttpStatusCode.NotFound, "Dieser Link ist nicht gültig")]
    [InlineData(CustomerInvoiceOutcome.Expired, HttpStatusCode.Gone, "Dieser Link ist abgelaufen")]
    [InlineData(CustomerInvoiceOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "Ihre Rechnung ist gerade nicht abrufbar")]
    public async Task Each_failure_state_renders_its_own_page_with_its_own_status(
        CustomerInvoiceOutcome outcome, HttpStatusCode expected, string heading)
    {
        var (response, html) = await GetAsync(new CustomerInvoiceResult(outcome, null));

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains("customer-card", html, StringComparison.Ordinal);
        Assert.Contains(heading, html, StringComparison.Ordinal);
        Assert.DoesNotContain("/pdf", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An invoice link cannot be re-issued (D99 covers Angebote only), so the expired page must not
    /// promise one. It points the customer to the company instead.
    /// </summary>
    [Fact]
    public async Task The_expired_page_promises_no_new_link()
    {
        var (_, html) = await GetAsync(CustomerInvoiceResult.Expired());

        Assert.Contains("Bitte wenden Sie sich an uns, wenn Sie die Rechnung erneut benötigen.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("neuen Link", html, StringComparison.Ordinal);
        Assert.DoesNotContain("senden wir", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_outage_does_not_tell_the_customer_their_link_is_invalid()
    {
        var (_, html) = await GetAsync(CustomerInvoiceResult.Unavailable());

        Assert.DoesNotContain("nicht gültig", html, StringComparison.Ordinal);
        Assert.Contains("nicht an Ihrem Link", html, StringComparison.Ordinal);
    }

    // ---- The token -----------------------------------------------------------------

    /// <summary>
    /// The token may appear only in the same-origin href of the PDF link, never in visible text, a
    /// form field or a query string, in every state the page can render.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("paid")]
    [InlineData("void")]
    [InlineData("no document")]
    [InlineData("not found")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    public async Task The_token_appears_only_in_the_same_origin_pdf_link(string state)
    {
        var result = state switch
        {
            "open" => Available(CustomerInvoiceStatus.Open),
            "paid" => Available(CustomerInvoiceStatus.Paid),
            "void" => Available(CustomerInvoiceStatus.Void),
            "no document" => Available(CustomerInvoiceStatus.Open, documentAvailable: false),
            "not found" => CustomerInvoiceResult.NotFound(),
            "expired" => CustomerInvoiceResult.Expired(),
            _ => CustomerInvoiceResult.Unavailable(),
        };

        var (_, html) = await GetAsync(result);

        TokenExposure.AssertOnlyInSameOriginLinks(html, Token, TokenExposure.InvoiceRoutes);
    }

    /// <summary>A token with characters a path segment must escape stays one segment in the link.</summary>
    [Fact]
    public async Task A_hand_edited_token_is_escaped_into_the_pdf_link()
    {
        factory.InvoiceResult = Available(CustomerInvoiceStatus.Open);
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/invoice/abc%20def")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/invoice/abc%20def/pdf\"", html, StringComparison.Ordinal);
    }

    // ---- Headers -------------------------------------------------------------------

    [Theory]
    [InlineData("open")]
    [InlineData("not found")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    public async Task Every_state_carries_the_token_route_headers(string state)
    {
        var result = state switch
        {
            "open" => Available(CustomerInvoiceStatus.Open),
            "not found" => CustomerInvoiceResult.NotFound(),
            "expired" => CustomerInvoiceResult.Expired(),
            _ => CustomerInvoiceResult.Unavailable(),
        };

        var (response, _) = await GetAsync(result);

        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.Contains("noindex", Header(response, "X-Robots-Tag"), StringComparison.Ordinal);
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    // ---- The customer surface ----------------------------------------------------------

    /// <summary>
    /// What the public read withholds stays withheld: no internal ids, no storage key, no API
    /// origin, no script. The page is German.
    /// </summary>
    [Fact]
    public async Task The_page_is_german_loads_no_script_and_names_no_api_origin_or_storage()
    {
        var (_, html) = await GetAsync(Available(CustomerInvoiceStatus.Open));

        Assert.Contains("<html lang=\"de\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.example.test", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("invoices/", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The approved boundary: one flag added to the public read and nothing else. The per-rate VAT
    /// lines, the description and the service period are on the PDF, not on this page (D114).
    /// </summary>
    [Fact]
    public async Task The_page_shows_the_vat_total_without_inventing_a_rate()
    {
        var (_, html) = await GetAsync(Available(CustomerInvoiceStatus.Open));

        Assert.Contains("zzgl. MwSt", html, StringComparison.Ordinal);
        Assert.DoesNotContain("19%", html, StringComparison.Ordinal);
        Assert.DoesNotContain("7%", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_supplied_text_is_encoded()
    {
        factory.InvoiceResult = CustomerInvoiceResult.Available(
            CustomerInvoiceBuilder.Open() with { CustomerName = "<script>alert(1)</script>" });
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync(PageUrl)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(", ", values);
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues)
            ? string.Join(", ", contentValues)
            : string.Empty;
    }
}
