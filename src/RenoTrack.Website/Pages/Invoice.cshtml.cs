using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The customer's invoice page (SRS FR-8.3, Wireframe A4, Phase 14 Slice 3b, D114), reached by the
/// token link emailed in <c>EmailMessageFactory.CreateInvoiceReady</c>. No account, no login:
/// possession of the link is the entire authorisation model (Architecture.md §7.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Thin, in the sense <c>CLAUDE.md</c> §22 requires of a controller.</b> It asks the API and
/// maps the outcome to a view state. Whether the link is valid or expired, what the invoice's status
/// is, and whether an archived PDF exists are all the API's answers. This page re-derives none of
/// them.
/// </para>
/// <para>
/// <b>Read-only.</b> PermissionMatrix.md §7 grants the customer viewing and the PDF download, and
/// nothing else. There is no invoice decision, so the link is never consumed.
/// </para>
/// </remarks>
public sealed class InvoiceModel(IPublicInvoiceClient client) : PageModel
{
    /// <summary>
    /// Bound from the route. Echoed only into the same-origin PDF link (<see cref="PdfUrl"/>), never
    /// into visible text, a form field, a log or an error message.
    /// </summary>
    [FromRoute]
    public string Token { get; set; } = string.Empty;

    public CustomerInvoiceOutcome Outcome { get; private set; } = CustomerInvoiceOutcome.Unavailable;

    public CustomerInvoice? Invoice { get; private set; }

    /// <summary>
    /// Where "Rechnung als PDF herunterladen" leads. The route lives under the token, as the Angebot
    /// decision routes do, so the credential stays in the path and out of a query string. The token
    /// is escaped because what arrived is whatever was in the address bar.
    /// </summary>
    public string PdfUrl => InvoicePdfModel.UrlFor(Token);

    /// <summary>
    /// Answers with the HTTP status that matches what the customer is told, so an outage is never
    /// cached or reported as a success.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await client.GetInvoiceAsync(Token, cancellationToken);

        Outcome = result.Outcome;
        Invoice = result.Invoice;

        Response.StatusCode = result.Outcome switch
        {
            CustomerInvoiceOutcome.Available => StatusCodes.Status200OK,
            CustomerInvoiceOutcome.NotFound => StatusCodes.Status404NotFound,
            CustomerInvoiceOutcome.Expired => StatusCodes.Status410Gone,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        return Page();
    }
}
