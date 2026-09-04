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
            HttpStatusCode.OK, """{"angebotNumber":"ANG-2026-00042"}""");

        var result = await ClientFor(handler).GetAngebotAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Available, result.Outcome);
        Assert.Equal("ANG-2026-00042", result.Angebot?.AngebotNumber);
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
            HttpStatusCode.OK, """{"angebotNumber":"ANG-2026-00042"}""");

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
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, """{"angebotNumber":"X"}""");

        var result = await ClientFor(handler).GetAngebotAsync(token, CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.NotFound, result.Outcome);
        Assert.Empty(handler.Requests);
    }
}
