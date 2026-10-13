using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The customer's download of their archived invoice PDF (Phase 14 Slice 3b, D114, Wireframe A4's
/// "[ Download PDF ]").
/// </summary>
/// <remarks>
/// <para>
/// <b>A server-side passthrough of the archive, never a render.</b> The bytes are the ones the API
/// serves from the file archived at send (BR-15): the document the customer was emailed, unchanged.
/// This Website has no PDF library and could not regenerate one if it tried.
/// </para>
/// <para>
/// <b>Buffered and checked before anything is sent</b> (<see cref="PublicInvoiceClient"/>). Only
/// because of that can every failure still become an honest German page with its own status, rather
/// than a broken download. The API's own messages never reach the customer.
/// </para>
/// <para>
/// <b>A Razor Page, so its route carries the <c>token</c> parameter</b>, and with it every
/// token-route protection keyed on that name: <c>Cache-Control: no-store</c>,
/// <c>X-Robots-Tag: noindex</c> and <c>Referrer-Policy: no-referrer</c> from
/// <c>CustomerSecurityHeaders</c>, and <c>MarketingPageGuard</c>'s refusal to put marketing metadata
/// on it.
/// </para>
/// </remarks>
public sealed class InvoicePdfModel(IPublicInvoiceClient client) : PageModel
{
    /// <summary>The route segment after the token. A literal, so it selects nothing.</summary>
    public const string PdfSegment = "pdf";

    [FromRoute]
    public string Token { get; set; } = string.Empty;

    public CustomerInvoiceDocumentOutcome Outcome { get; private set; } = CustomerInvoiceDocumentOutcome.Unavailable;

    /// <summary>The invoice page for this link, offered from the states where it can still help.</summary>
    public string InvoiceUrl => $"/invoice/{Uri.EscapeDataString(Token)}";

    /// <summary>The one definition of this route's address, shared with the page that links here.</summary>
    public static string UrlFor(string token) => $"/invoice/{Uri.EscapeDataString(token)}/{PdfSegment}";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await client.GetInvoiceDocumentAsync(Token, cancellationToken);

        Outcome = result.Outcome;

        if (result.Outcome == CustomerInvoiceDocumentOutcome.Available)
        {
            // An attachment, as the email's was, under the name the email used. The headers that keep
            // it out of caches and indexes are added by CustomerSecurityHeaders on the way out.
            return File(result.Document!.Content, CustomerInvoiceDocument.PdfContentType, result.Document.FileName);
        }

        Response.StatusCode = result.Outcome switch
        {
            CustomerInvoiceDocumentOutcome.NotFound => StatusCodes.Status404NotFound,
            CustomerInvoiceDocumentOutcome.Expired => StatusCodes.Status410Gone,

            // The approved answer for a valid link with no archived document: this Website's own
            // "no PDF available" page, as a 404 for the document that does not exist.
            CustomerInvoiceDocumentOutcome.NoDocument => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        return Page();
    }
}
