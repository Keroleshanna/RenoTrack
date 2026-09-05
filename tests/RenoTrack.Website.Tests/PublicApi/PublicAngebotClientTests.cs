using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// The Website's single boundary to the API (D97). These tests own the one mapping in the system
/// that turns an HTTP status into something a customer is told, so they cover every branch of it —
/// including the ones a happy-path integration test would never reach.
/// </summary>
/// <remarks>
/// No server and no database: a stub <see cref="HttpMessageHandler"/> answers, so every case runs on
/// any operating system. That matters here beyond speed — the LocalDB-backed suites cannot run on
/// this project's Linux CI job (D56), and this boundary is exactly the part of Slice 2 that must be
/// verifiable everywhere.
/// </remarks>
public sealed class PublicAngebotClientTests
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private static PublicAngebotClient ClientFor(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") },
            NullLogger<PublicAngebotClient>.Instance);

    // ---- Outcomes ----------------------------------------------------------

    [Fact]
    public async Task A_200_yields_the_angebot()
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson());

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Available, result.Outcome);
        Assert.Equal(CustomerAngebotBuilder.Number, result.Angebot?.AngebotNumber);
    }

    /// <summary>
    /// The whole document survives the boundary, nested and in order — the round trip Slice 3 exists
    /// to make real, and the thing that would break silently if either side changed shape.
    /// </summary>
    [Fact]
    public async Task A_200_yields_every_part_of_the_document()
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson());

        var angebot = (await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None)).Angebot;

        Assert.NotNull(angebot);
        Assert.Equal(CustomerAngebotDecision.Pending, angebot.Decision);
        Assert.Equal(1_650.00m, angebot.NetTotal);
        Assert.Equal(1_951.50m, angebot.GrossTotal);

        Assert.Equal([16m, 19m], angebot.VatBreakdown.Select(line => line.Rate));
        Assert.Equal([64.00m, 237.50m], angebot.VatBreakdown.Select(line => line.VatAmount));

        Assert.Equal(["Abriss", "Baustelleneinrichtung"], angebot.Sections.Select(section => section.Title));
        Assert.Equal([1_250.00m, 400.00m], angebot.Sections.Select(section => section.Subtotal));

        var firstSection = angebot.Sections[0];
        Assert.Equal(
            ["Wände abbrechen", "Schutt entsorgen"],
            firstSection.Items.Select(item => item.Description));

        var firstItem = firstSection.Items[0];
        Assert.Equal("Nichttragend, inkl. Entsorgung", firstItem.Specification);
        Assert.Equal(10m, firstItem.Quantity);
        Assert.Equal("m2", firstItem.Unit);
        Assert.Equal(25.00m, firstItem.UnitPrice);
        Assert.Equal(250.00m, firstItem.LineTotal);

        // A null specification is a normal line, not a broken one.
        Assert.Null(firstSection.Items[1].Specification);
        Assert.Equal(2.5m, firstSection.Items[1].Quantity);
    }

    /// <summary>
    /// A decided Angebot stays readable (BR-4) and its decision reaches the page, which is what
    /// lets the page avoid rendering it identically to a pending one.
    /// </summary>
    [Theory]
    [InlineData("Approved", CustomerAngebotDecision.Approved)]
    [InlineData("Rejected", CustomerAngebotDecision.Rejected)]
    [InlineData("Pending", CustomerAngebotDecision.Pending)]
    public async Task The_decision_state_crosses_the_boundary(string wireValue, CustomerAngebotDecision expected)
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson(wireValue));

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Available, result.Outcome);
        Assert.Equal(expected, result.Angebot?.Decision);
    }

    /// <summary>
    /// An unrecognised decision is reported as an outage rather than defaulting to
    /// <see cref="CustomerAngebotDecision.Pending"/>. Telling a customer their recorded answer is
    /// still pending would be a wrong statement about their own decision — worse than an honest
    /// "not available right now".
    /// </summary>
    [Fact]
    public async Task An_unrecognised_decision_is_an_outage_not_a_pending_angebot()
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson("SomethingNew"));

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Angebot);
    }

    /// <summary>
    /// The API answers 404 for an unknown token and for a token belonging to an Invoice,
    /// deliberately indistinguishably. The Website must not undo that by inferring a difference.
    /// </summary>
    [Fact]
    public async Task A_404_is_a_not_found_link()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.NotFound);

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.NotFound, result.Outcome);
        Assert.Null(result.Angebot);
    }

    /// <summary>410 is expiry specifically, which the customer is told honestly (SRS FR-6.4).</summary>
    [Fact]
    public async Task A_410_is_an_expired_link()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.Gone);

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Expired, result.Outcome);
    }

    /// <summary>
    /// An outage must never be reported as an invalid link: one invites the customer back, the
    /// other tells them to give up on a link that is actually fine.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Any_other_status_is_unavailable_never_a_bad_link(HttpStatusCode statusCode)
    {
        var handler = StubHttpMessageHandler.Responding(statusCode);

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused."));

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, result.Outcome);
    }

    /// <summary>
    /// HttpClient surfaces its own timeout as TaskCanceledException with no cancellation requested
    /// by the caller — which must become an outcome, not an unhandled exception on a customer page.
    /// </summary>
    [Fact]
    public async Task A_client_timeout_is_unavailable()
    {
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("The request timed out."));

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, result.Outcome);
    }

    /// <summary>
    /// A 200 whose body is not the agreed contract is an integration fault. It is reported as an
    /// outage rather than as a missing quote, because the quote may well exist.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"angebotNumber":""}""")]
    [InlineData("not json at all")]
    // A document with no sections array and one with no VAT array are contract breaks, not
    // documents: the API always emits an array, so `[]` arrives as an empty list and never as null.
    [InlineData("""{"angebotNumber":"ANG-1","decision":"Pending","netTotal":0,"grossTotal":0,"vatBreakdown":[]}""")]
    [InlineData("""{"angebotNumber":"ANG-1","decision":"Pending","netTotal":0,"grossTotal":0,"sections":[]}""")]
    public async Task A_200_with_an_unusable_body_is_unavailable(string body)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, body);

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, result.Outcome);
    }

    /// <summary>
    /// The caller giving up (the customer navigated away) is not an outage and must not be logged
    /// or rendered as one — it propagates so the framework abandons the request.
    /// </summary>
    [Fact]
    public async Task A_cancelled_request_propagates_rather_than_becoming_an_outcome()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = StubHttpMessageHandler.Throwing(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ClientFor(handler).GetAngebotAsync(Token, cts.Token));
    }

    // ---- Request shape -----------------------------------------------------

    [Fact]
    public async Task The_request_targets_the_public_angebot_route()
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson());

        await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"https://api.example.test/api/v1/public/angebote/{Token}",
            request.RequestUri?.AbsoluteUri);
    }

    /// <summary>
    /// Whatever was in the customer's address bar arrives here, not necessarily a token this system
    /// issued. Escaping keeps a hand-edited value one path segment instead of letting it rewrite the
    /// request into a different resource.
    /// </summary>
    [Fact]
    public async Task A_token_containing_path_characters_cannot_rewrite_the_request()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.NotFound);

        await ClientFor(handler).GetAngebotAsync("../../leads?x=1", CancellationToken.None);

        var uri = Assert.Single(handler.Requests).RequestUri;
        Assert.NotNull(uri);

        // AbsolutePath, not ToString(): ToString() unescapes, which would hide the very escaping
        // this test exists to prove. The separators stay percent-encoded, so the whole value remains
        // one path segment under the public route.
        Assert.StartsWith("/api/v1/public/angebote/", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/leads", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Empty(uri.Query);
    }

    /// <summary>
    /// An empty token cannot identify a link, and sending it would append nothing to the path —
    /// turning a customer's truncated link into a request for a different resource entirely.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_token_is_refused_without_calling_the_api(string token)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, CustomerAngebotBuilder.TypicalJson());

        var result = await ClientFor(handler).GetAngebotAsync(token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.NotFound, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    // ---- Recording a decision (Slice 4) ------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.OK, CustomerDecisionOutcome.Recorded)]
    [InlineData(HttpStatusCode.NotFound, CustomerDecisionOutcome.NotFound)]
    [InlineData(HttpStatusCode.Gone, CustomerDecisionOutcome.Expired)]
    [InlineData(HttpStatusCode.Conflict, CustomerDecisionOutcome.AlreadyDecided)]
    [InlineData(HttpStatusCode.BadRequest, CustomerDecisionOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerDecisionOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CustomerDecisionOutcome.Unavailable)]
    public async Task A_decision_maps_every_documented_status(
        HttpStatusCode status,
        CustomerDecisionOutcome expected)
    {
        var handler = StubHttpMessageHandler.Responding(status);

        var outcome = await ClientFor(handler)
            .RecordDecisionAsync(Token, CustomerDecisionChoice.Approve, CancellationToken.None);

        Assert.Equal(expected, outcome);
    }

    /// <summary>
    /// 409 is the only status whose meaning differs between the two endpoints: the read has no
    /// "already used" outcome at all, because BR-4 keeps viewing open after a decision.
    /// </summary>
    [Fact]
    public async Task A_409_is_already_decided_and_never_an_outage()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.Conflict);

        var outcome = await ClientFor(handler)
            .RecordDecisionAsync(Token, CustomerDecisionChoice.Reject, CancellationToken.None);

        Assert.Equal(CustomerDecisionOutcome.AlreadyDecided, outcome);
        Assert.NotEqual(CustomerDecisionOutcome.Unavailable, outcome);
    }

    [Theory]
    [InlineData(CustomerDecisionChoice.Approve, "Approve")]
    [InlineData(CustomerDecisionChoice.Reject, "Reject")]
    public async Task A_decision_posts_the_token_in_the_route_and_the_choice_in_the_body(
        CustomerDecisionChoice choice,
        string expectedName)
    {
        string? body = null;
        Uri? uri = null;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            uri = request.RequestUri;
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await ClientFor(handler).RecordDecisionAsync(Token, choice, CancellationToken.None);

        Assert.Equal($"/api/v1/public/angebote/{Token}/decision", uri?.AbsolutePath);

        // The enum crosses as a name, matching the API's JsonStringEnumConverter (D61) — an ordinal
        // would silently change meaning if anyone reordered either enum.
        Assert.Contains(expectedName, body ?? string.Empty, StringComparison.Ordinal);

        // The token is a route value on both sides. It must not also travel in the body, where it
        // would end up in request logs that the route form is deliberately kept out of.
        Assert.DoesNotContain(Token, body ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Refused before a socket is opened, exactly as the read refuses it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_token_records_nothing(string token)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK);

        var outcome = await ClientFor(handler)
            .RecordDecisionAsync(token, CustomerDecisionChoice.Approve, CancellationToken.None);

        Assert.Equal(CustomerDecisionOutcome.NotFound, outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_unreachable_api_is_an_outage_not_a_broken_link()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));

        var outcome = await ClientFor(handler)
            .RecordDecisionAsync(Token, CustomerDecisionChoice.Approve, CancellationToken.None);

        Assert.Equal(CustomerDecisionOutcome.Unavailable, outcome);
    }
}
