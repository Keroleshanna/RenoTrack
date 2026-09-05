using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The two-step decision flow as it is actually served: the buttons on the document, the
/// server-rendered confirmation, and the POST that finally records the answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load-bearing test in this file is
/// <see cref="The_confirmation_step_records_nothing"/>.</b> Everything else here checks that the
/// flow works; that one checks that the first half of it cannot mutate, which is the property the
/// two-step design exists for and the one that would be silently lost by a refactor.
/// </para>
/// <para>
/// No database and no API process, so this runs in CI's Linux job like the rest of the customer
/// suite (D56).
/// </para>
/// </remarks>
public sealed partial class AngebotDecisionPageTests : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";
    private const string ApproveUrl = $"/angebot/{Token}/entscheidung/annehmen";
    private const string RejectUrl = $"/angebot/{Token}/entscheidung/ablehnen";
    private const string DocumentUrl = $"/angebot/{Token}";

    private readonly CustomerWebsiteFactory factory;

    public AngebotDecisionPageTests(CustomerWebsiteFactory factory)
    {
        this.factory = factory;
        factory.RequestedTokens.Clear();
        factory.RecordedDecisions.Clear();
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical());
        factory.DecisionOutcome = CustomerDecisionOutcome.Recorded;
    }

    /// <summary>Redirects are the thing under test here, so they are never followed silently.</summary>
    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ---- Step one: the buttons on the document -----------------------------

    [Fact]
    public async Task A_pending_angebot_offers_both_choices()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync(DocumentUrl);

        Assert.Contains("Angebot annehmen", html, StringComparison.Ordinal);
        Assert.Contains("Angebot ablehnen", html, StringComparison.Ordinal);
        Assert.Contains(ApproveUrl, html, StringComparison.Ordinal);
        Assert.Contains(RejectUrl, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// BR-4 makes the decision final and no reopen path exists, so an answered Angebot offers
    /// nothing to click — §23's "a state whose next step is not a button gets an explanation".
    /// </summary>
    [Theory]
    [InlineData(CustomerAngebotDecision.Approved)]
    [InlineData(CustomerAngebotDecision.Rejected)]
    public async Task A_decided_angebot_offers_neither_choice(CustomerAngebotDecision decision)
    {
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical(decision));
        using var client = CreateClient();

        var html = await client.GetStringAsync(DocumentUrl);

        Assert.DoesNotContain("entscheidung", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Angebot annehmen", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Angebot ablehnen", html, StringComparison.Ordinal);
    }

    // ---- Step two: the confirmation ----------------------------------------

    [Theory]
    [InlineData(ApproveUrl, "Annahme bestätigen")]
    [InlineData(RejectUrl, "Ablehnung bestätigen")]
    public async Task The_confirmation_names_the_angebot_and_offers_both_ways_out(
        string url,
        string confirmLabel)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(CustomerAngebotBuilder.Number, html, StringComparison.Ordinal);
        Assert.Contains(confirmLabel, html, StringComparison.Ordinal);
        Assert.Contains("Abbrechen", html, StringComparison.Ordinal);
        Assert.Contains(DocumentUrl, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The reason the first step is a GET.</b> A customer who clicks "annehmen" and then walks
    /// away, or refreshes, or is followed by a link-preview fetcher, must not have decided anything.
    /// </summary>
    [Theory]
    [InlineData(ApproveUrl)]
    [InlineData(RejectUrl)]
    public async Task The_confirmation_step_records_nothing(string url)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.RecordedDecisions);
    }

    /// <summary>Nothing is left to confirm, so the customer is sent to what was actually recorded.</summary>
    [Fact]
    public async Task Confirming_an_already_decided_angebot_redirects_to_the_document()
    {
        factory.Result = CustomerAngebotResult.Available(
            CustomerAngebotBuilder.Typical(CustomerAngebotDecision.Approved));
        using var client = CreateClient();

        using var response = await client.GetAsync(RejectUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DocumentUrl, response.Headers.Location?.ToString());
        Assert.Empty(factory.RecordedDecisions);
    }

    /// <summary>Anything that is not one of the two documented choices is not a route at all.</summary>
    [Theory]
    [InlineData("vielleicht")]
    [InlineData("approve")]
    [InlineData("annehmen-bitte")]
    public async Task An_unknown_choice_segment_is_not_routable(string choice)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}/entscheidung/{choice}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.RecordedDecisions);
    }

    /// <summary>
    /// <b>A defect this test found rather than confirmed.</b> ASP.NET matches route constraints
    /// case-insensitively, so a capitalised link — from a mail client that title-cases URLs, or a
    /// customer retyping one — routes here perfectly happily. The first implementation compared the
    /// segment ordinally and fell through to the <c>else</c>, so <c>/entscheidung/Annehmen</c> would
    /// have <b>rejected</b> the Angebot the customer was accepting. There is no worse failure
    /// available in this flow.
    /// </summary>
    [Theory]
    [InlineData("Annehmen", CustomerDecisionChoice.Approve)]
    [InlineData("ANNEHMEN", CustomerDecisionChoice.Approve)]
    [InlineData("Ablehnen", CustomerDecisionChoice.Reject)]
    public async Task A_capitalised_route_records_the_choice_it_names(
        string choice,
        CustomerDecisionChoice expected)
    {
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, $"/angebot/{Token}/entscheidung/{choice}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, Assert.Single(factory.RecordedDecisions).Choice);
    }

    // ---- Step three: the decision ------------------------------------------

    [Theory]
    [InlineData(ApproveUrl, CustomerDecisionChoice.Approve)]
    [InlineData(RejectUrl, CustomerDecisionChoice.Reject)]
    public async Task Confirming_records_the_choice_named_by_the_route(
        string url,
        CustomerDecisionChoice expected)
    {
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var recorded = Assert.Single(factory.RecordedDecisions);
        Assert.Equal(Token, recorded.Token);
        Assert.Equal(expected, recorded.Choice);
    }

    /// <summary>
    /// Post-Redirect-Get. Without it a refresh re-posts a consumed link and shows a failure for an
    /// action that succeeded.
    /// </summary>
    [Fact]
    public async Task A_recorded_decision_redirects_to_the_document()
    {
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, ApproveUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DocumentUrl, response.Headers.Location?.ToString());
    }

    /// <summary>
    /// Two customers, one link. The loser must be shown the decision that was actually persisted —
    /// never a page about the answer they attempted.
    /// </summary>
    [Fact]
    public async Task A_link_answered_by_someone_else_shows_the_persisted_decision()
    {
        factory.DecisionOutcome = CustomerDecisionOutcome.AlreadyDecided;
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, RejectUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(DocumentUrl, response.Headers.Location?.ToString());

        // The redirect target re-reads, and shows what the API says — approved, though this
        // customer pressed "ablehnen".
        factory.Result = CustomerAngebotResult.Available(
            CustomerAngebotBuilder.Typical(CustomerAngebotDecision.Approved));
        var html = await client.GetStringAsync(DocumentUrl);

        Assert.Contains("Sie haben dieses Angebot angenommen.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("abgelehnt", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CustomerDecisionOutcome.NotFound, HttpStatusCode.NotFound, "nicht gültig")]
    [InlineData(CustomerDecisionOutcome.Expired, HttpStatusCode.Gone, "abgelaufen")]
    [InlineData(CustomerDecisionOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "nicht gespeichert werden")]
    public async Task A_refused_decision_answers_with_its_own_status_and_message(
        CustomerDecisionOutcome outcome,
        HttpStatusCode expectedStatus,
        string expectedText)
    {
        factory.DecisionOutcome = outcome;
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, ApproveUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(expectedText, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An outage on this route means something the read route's outage never does: the decision may
    /// have been recorded before the failure. So the customer is asked to look again, never told
    /// their answer was lost.
    /// </summary>
    [Fact]
    public async Task An_outage_does_not_claim_the_decision_was_not_recorded()
    {
        factory.DecisionOutcome = CustomerDecisionOutcome.Unavailable;
        using var client = CreateClient();

        using var response = await PostConfirmationAsync(client, ApproveUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("ob Ihre Antwort angekommen ist", html, StringComparison.Ordinal);
        Assert.DoesNotContain("nicht gültig", html, StringComparison.Ordinal);
    }

    // ---- Antiforgery and the token -----------------------------------------

    /// <summary>
    /// The only form on any customer page, so this is the only place the protection applies — and
    /// the only place it could be lost without anything else breaking.
    /// </summary>
    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_refused()
    {
        using var client = CreateClient();

        using var response = await client.PostAsync(
            ApproveUrl,
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.RecordedDecisions);
    }

    /// <summary>
    /// The URL is the credential. It stays in the route: never a hidden field, never a query string,
    /// never the POST body, never visible text — so the antiforgery token is the only hidden input
    /// this page carries.
    /// </summary>
    [Theory]
    [InlineData(ApproveUrl)]
    [InlineData(RejectUrl)]
    public async Task The_link_token_appears_only_where_it_must(string url)
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync(url);

        TokenExposure.AssertOnlyInSameOriginLinks(html, Token);
    }

    /// <summary>
    /// The confirmation page carries a credential in its URL exactly as the document does, and the
    /// headers are keyed on the route parameter rather than the path — this proves that keying
    /// actually reaches the new route.
    /// </summary>
    [Fact]
    public async Task The_confirmation_page_carries_the_token_route_headers()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(ApproveUrl);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("noindex, nofollow, noarchive", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    /// <summary>No script reaches a customer page (D97), and a form does not change that.</summary>
    [Fact]
    public async Task The_confirmation_page_loads_no_script()
    {
        using var client = CreateClient();

        var html = await client.GetStringAsync(ApproveUrl);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>
    /// Walks the flow as a browser does: GET the confirmation, take the antiforgery token the form
    /// carries, POST it back. Cookies flow through the factory's own handler.
    /// </summary>
    private static async Task<HttpResponseMessage> PostConfirmationAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var token = AntiforgeryField().Match(html);
        Assert.True(token.Success, "The confirmation form carried no antiforgery token.");

        return await client.PostAsync(
            url,
            new FormUrlEncodedContent(
                [new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Value)]));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
