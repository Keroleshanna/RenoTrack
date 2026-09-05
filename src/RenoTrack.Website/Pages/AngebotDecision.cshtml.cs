using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Pages;

/// <summary>
/// The customer's confirmation step and the decision itself (SRS FR-6.3, Wireframe A3's two
/// buttons).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two steps, and the first one cannot mutate.</b> The document page's buttons link here with
/// <c>GET</c>, which renders a confirmation; only the <c>POST</c> from that page's Bestätigen
/// button records anything. The first step is a GET precisely so that "the confirmation page
/// consumes no token and records no decision" is a property of the HTTP method rather than of care
/// taken inside a handler. A decision is irreversible under BR-4, which is exactly the case D83
/// says must be confirmed before it runs.
/// </para>
/// <para>
/// <b>The decision travels in the route, like the token.</b> No hidden field, no query string, no
/// cookie, no <c>TempData</c>, no session — so there is no client-supplied state a customer could
/// edit into a different decision than the one whose confirmation page they read, and no
/// JavaScript anywhere (D97).
/// </para>
/// <para>
/// <b>Thin, in the sense §22 requires of a controller.</b> Whether the link is valid, expired or
/// already used is the API's answer; this page re-derives none of it. The one state it reads —
/// whether the Angebot is still <c>Pending</c> — is not a rule being duplicated but the answer to
/// "is there anything here to confirm?", and the API refuses regardless if it is wrong.
/// </para>
/// </remarks>
public sealed class AngebotDecisionModel(IPublicAngebotClient client) : PageModel
{
    /// <summary>
    /// Bound from the route. Deliberately not echoed into the page, any form field, any link, any
    /// log or any error text — the URL is the credential.
    /// </summary>
    [FromRoute]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Bound from the route: <c>annehmen</c> or <c>ablehnen</c>. Constrained by the route template,
    /// so anything else is a 404 from routing and never reaches this model.
    /// </summary>
    [FromRoute]
    public string Choice { get; set; } = string.Empty;

    public CustomerAngebot? Angebot { get; private set; }

    /// <summary>The decision this page is asking the customer to confirm.</summary>
    public CustomerDecisionChoice DecisionChoice => ChoiceFromRoute(Choice);

    public bool IsApproval => DecisionChoice == CustomerDecisionChoice.Approve;

    /// <summary>Where the document lives, for the Abbrechen link and every redirect.</summary>
    public string DocumentUrl => $"/angebot/{Uri.EscapeDataString(Token)}";

    /// <summary>
    /// Renders the confirmation. Records nothing, consumes nothing, and calls no decision endpoint.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await client.GetAngebotAsync(Token, cancellationToken);

        switch (result.Outcome)
        {
            case CustomerAngebotOutcome.Available when result.Angebot!.Decision == CustomerAngebotDecision.Pending:
                Angebot = result.Angebot;
                return Page();

            // Already answered. There is nothing to confirm, so the customer is sent to the
            // document, which shows what was actually recorded rather than what they were about to
            // choose.
            case CustomerAngebotOutcome.Available:
                return Redirect(DocumentUrl);

            default:
                return Failure(result.Outcome);
        }
    }

    /// <summary>
    /// Records the decision, then redirects to the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Post-Redirect-Get, and not only for tidiness.</b> Without the redirect a refresh, or a
    /// back-then-forward, re-posts a link that has just been consumed — showing the customer a
    /// failure for an action that in fact succeeded.
    /// </para>
    /// <para>
    /// <b><see cref="CustomerDecisionOutcome.AlreadyDecided"/> redirects to exactly the same
    /// place</b>, which is how the losing side of a race is shown the winning decision: the
    /// document page re-reads from the API, so if one customer approved and another rejected, the
    /// second is shown the Angebot as <i>approved</i>. The API and the database are authoritative;
    /// what this request attempted is not. No "already answered" sentence is carried across the
    /// redirect, because carrying one means cookie-backed <c>TempData</c> — client-side state this
    /// design exists to avoid — for a message the banner already makes true.
    /// </para>
    /// </remarks>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var outcome = await client.RecordDecisionAsync(Token, DecisionChoice, cancellationToken);

        return outcome switch
        {
            CustomerDecisionOutcome.Recorded => Redirect(DocumentUrl),

            // The link may in fact have been consumed by this very request before the failure —
            // so the customer is sent to re-read rather than told the decision did not happen.
            CustomerDecisionOutcome.AlreadyDecided => Redirect(DocumentUrl),

            CustomerDecisionOutcome.NotFound => Failure(CustomerAngebotOutcome.NotFound),
            CustomerDecisionOutcome.Expired => Failure(CustomerAngebotOutcome.Expired),
            _ => Failure(CustomerAngebotOutcome.Unavailable),
        };
    }

    /// <summary>
    /// Renders one of the three failure pages the document route already uses, with the matching
    /// status — so a proxy or a crawler is told the same thing the reader is, and an outage is
    /// never reported as a success.
    /// </summary>
    private IActionResult Failure(CustomerAngebotOutcome outcome)
    {
        FailureOutcome = outcome;
        Response.StatusCode = outcome switch
        {
            CustomerAngebotOutcome.NotFound => StatusCodes.Status404NotFound,
            CustomerAngebotOutcome.Expired => StatusCodes.Status410Gone,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        return Page();
    }

    /// <summary>
    /// Non-null when the page is rendering a failure instead of the confirmation.
    /// </summary>
    public CustomerAngebotOutcome? FailureOutcome { get; private set; }

    /// <summary>
    /// The route's German segment as the choice it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Compared case-insensitively, and that is not cosmetic.</b> ASP.NET's route constraints
    /// match case-insensitively, so <c>/entscheidung/Annehmen</c> — a capitalised link from a mail
    /// client, or a hand-retyped URL — routes here perfectly happily. An ordinal comparison would
    /// then fall through to the <c>else</c> branch and <b>reject the Angebot the customer was
    /// trying to accept</b>. Found by a test asserting the capitalised form was unroutable; it was
    /// routable, and the mapping was wrong.
    /// </para>
    /// <para>
    /// The fallback is otherwise unreachable through routing, which the template constrains — it
    /// exists because a method that maps a string must answer for every string. It resolves to
    /// <c>Reject</c> deliberately: of the two, silently declining is the failure a customer can
    /// still recover from by contacting us.
    /// </para>
    /// </remarks>
    internal static CustomerDecisionChoice ChoiceFromRoute(string choice) =>
        string.Equals(choice, ApproveSegment, StringComparison.OrdinalIgnoreCase)
            ? CustomerDecisionChoice.Approve
            : CustomerDecisionChoice.Reject;

    internal const string ApproveSegment = "annehmen";
    internal const string RejectSegment = "ablehnen";
}
