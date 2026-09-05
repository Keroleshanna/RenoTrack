using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The customer's Angebot page (SRS FR-6.2, Wireframe A3), reached by the token link emailed in
/// <c>EmailMessageFactory.CreateAngebotReady</c>. No account, no login, no session — possession of
/// the link is the entire authorisation model (Architecture.md §7.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Thin, in the sense <c>CLAUDE.md</c> §22 requires of a controller.</b> It validates nothing,
/// decides nothing, and contains no business rule: it asks the API and maps the outcome to a view
/// state. Whether a link is valid, expired or consumed is the API's answer, and re-deriving any of
/// it here would be a second definition of rules the Domain already owns.
/// </para>
/// <para>
/// <b>Slice 2 renders the skeleton only.</b> The sections, line items, totals and VAT breakdown
/// Wireframe A3 shows are Slice 3; the Annehmen/Ablehnen buttons and the rejection reason are
/// Slice 4. What exists here is the route, the round trip, and every state the customer can land in.
/// </para>
/// </remarks>
public sealed class AngebotModel(IPublicAngebotClient client) : PageModel
{
    /// <summary>
    /// Bound from the route. Deliberately not echoed into the page, any log, or any error text.
    /// </summary>
    [FromRoute]
    public string Token { get; set; } = string.Empty;

    public CustomerAngebotOutcome Outcome { get; private set; } = CustomerAngebotOutcome.Unavailable;

    public CustomerAngebot? Angebot { get; private set; }

    /// <summary>
    /// Where the "Angebot annehmen" / "Angebot ablehnen" buttons lead: the confirmation step, whose
    /// POST performs the decision.
    /// </summary>
    /// <remarks>
    /// Built here rather than in the view so the route segments have exactly one definition, shared
    /// with the page that consumes them. The token is escaped for the same reason the client
    /// escapes it — what arrives is whatever was in the address bar, not necessarily a token this
    /// system issued.
    /// </remarks>
    public string DecisionUrl(bool approve) =>
        $"/angebot/{Uri.EscapeDataString(Token)}/entscheidung/"
        + (approve ? AngebotDecisionModel.ApproveSegment : AngebotDecisionModel.RejectSegment);

    /// <summary>
    /// Answers with the HTTP status that matches what the customer is being told, so the page is
    /// honest to a proxy or a crawler as well as to a reader — and so an outage is never cached or
    /// reported as a success.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await client.GetAngebotAsync(Token, cancellationToken);

        Outcome = result.Outcome;
        Angebot = result.Angebot;

        return result.Outcome switch
        {
            CustomerAngebotOutcome.Available => Page(),
            CustomerAngebotOutcome.NotFound => Page404(),
            CustomerAngebotOutcome.Expired => PageWithStatus(StatusCodes.Status410Gone),
            _ => PageWithStatus(StatusCodes.Status503ServiceUnavailable),
        };
    }

    private IActionResult Page404() => PageWithStatus(StatusCodes.Status404NotFound);

    private IActionResult PageWithStatus(int statusCode)
    {
        Response.StatusCode = statusCode;
        return Page();
    }
}
