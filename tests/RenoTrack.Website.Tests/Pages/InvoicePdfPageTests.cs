using System.Net;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// <c>/invoice/{token}/pdf</c> as it is actually served (Phase 14 Slice 3b, D114). It passes on the
/// archived bytes as an attachment under the name the email used. Every failure becomes this
/// Website's own German page with its own status. The token-route headers apply to the file as
/// much as to the pages.
/// </summary>
public sealed class InvoicePdfPageTests : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "Pdf9-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";
    private const string PdfUrl = $"/invoice/{Token}/pdf";

    private readonly CustomerWebsiteFactory factory;

    public InvoicePdfPageTests(CustomerWebsiteFactory factory)
    {
        this.factory = factory;
        factory.RequestedInvoiceTokens.Clear();
        factory.InvoiceDocumentResult = CustomerInvoiceDocumentResult.Available(CustomerInvoiceBuilder.Document());
    }

    private async Task<HttpResponseMessage> GetAsync(CustomerInvoiceDocumentResult result)
    {
        factory.InvoiceDocumentResult = result;
        using var client = factory.CreateClient();
        return await client.GetAsync(PdfUrl);
    }

    // ---- The document --------------------------------------------------------------

    [Fact]
    public async Task The_pdf_is_served_byte_for_byte_as_an_attachment()
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Available(CustomerInvoiceBuilder.Document()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(
            $"Rechnung-{CustomerInvoiceBuilder.Number}.pdf",
            response.Content.Headers.ContentDisposition.FileName?.Trim('"'));
        Assert.Equal(CustomerInvoiceBuilder.PdfBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(Token, Assert.Single(factory.RequestedInvoiceTokens));
    }

    /// <summary>
    /// The file carries the token-route headers too: no shared or browser cache keeps it, no
    /// crawler indexes it, and it is only ever treated as the PDF it declares.
    /// </summary>
    [Fact]
    public async Task The_pdf_response_carries_the_token_route_headers()
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Available(CustomerInvoiceBuilder.Document()));

        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.Contains("noindex", Header(response, "X-Robots-Tag"), StringComparison.Ordinal);
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    /// <summary>No header of the file response names the token, the API or a storage location.</summary>
    [Fact]
    public async Task No_header_of_the_pdf_response_carries_the_token_or_the_api()
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Available(CustomerInvoiceBuilder.Document()));

        var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers)
            .Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));
        Assert.DoesNotContain(Token, headers, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example.test", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoices/", headers, StringComparison.Ordinal);
    }

    // ---- Failure states ------------------------------------------------------------------

    [Theory]
    [InlineData(CustomerInvoiceDocumentOutcome.NotFound, HttpStatusCode.NotFound, "Dieser Link ist nicht gültig")]
    [InlineData(CustomerInvoiceDocumentOutcome.Expired, HttpStatusCode.Gone, "Dieser Link ist abgelaufen")]
    [InlineData(CustomerInvoiceDocumentOutcome.NoDocument, HttpStatusCode.NotFound, "Kein PDF verfügbar")]
    [InlineData(CustomerInvoiceDocumentOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "Das PDF ist gerade nicht abrufbar")]
    public async Task Each_failure_renders_the_websites_own_page_with_its_own_status(
        CustomerInvoiceDocumentOutcome outcome, HttpStatusCode expected, string heading)
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Of(outcome));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("customer-card", html, StringComparison.Ordinal);
        Assert.Contains(heading, html, StringComparison.Ordinal);
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task The_no_document_page_uses_the_approved_copy()
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.NoDocument));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Ein PDF dieser Rechnung ist hier nicht verfügbar.", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/invoice/{Token}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_expired_page_promises_no_new_link()
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Expired));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Bitte wenden Sie sich an uns, wenn Sie die Rechnung erneut benötigen.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("neuen Link", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CustomerInvoiceDocumentOutcome.NotFound)]
    [InlineData(CustomerInvoiceDocumentOutcome.Expired)]
    [InlineData(CustomerInvoiceDocumentOutcome.NoDocument)]
    [InlineData(CustomerInvoiceDocumentOutcome.Unavailable)]
    public async Task Every_failure_page_carries_the_token_route_headers_and_keeps_the_token_in_links_only(
        CustomerInvoiceDocumentOutcome outcome)
    {
        using var response = await GetAsync(CustomerInvoiceDocumentResult.Of(outcome));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.Contains("noindex", Header(response, "X-Robots-Tag"), StringComparison.Ordinal);
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        TokenExposure.AssertOnlyInSameOriginLinks(html, Token, TokenExposure.InvoiceRoutes);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The literal segment selects nothing. Like every ASP.NET route literal it matches
    /// case-insensitively (CLAUDE.md §24), and either case serves the same document.
    /// </summary>
    [Fact]
    public async Task The_pdf_segment_matches_case_insensitively()
    {
        factory.InvoiceDocumentResult = CustomerInvoiceDocumentResult.Available(CustomerInvoiceBuilder.Document());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/invoice/{Token}/PDF");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CustomerInvoiceBuilder.PdfBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void The_pdf_link_is_built_from_one_definition()
    {
        Assert.Equal("/invoice/a%2Fb/pdf", RenoTrack.Website.Pages.InvoicePdfModel.UrlFor("a/b"));
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
