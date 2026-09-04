using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenoTrack.Website.Pages;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The page model maps an outcome to a view state and an HTTP status, and does nothing else — it
/// validates nothing and decides nothing, in the sense <c>CLAUDE.md</c> §22 requires of a
/// controller.
/// </summary>
/// <remarks>
/// The status codes are the point of these tests. A page that tells the customer their quote is
/// unreachable while answering 200 is lying to every proxy, crawler and monitor that sees it, and
/// an outage reported as 200 is one that nothing will alert on.
/// </remarks>
public sealed class AngebotModelTests
{
    private sealed class StubClient(CustomerAngebotResult result) : IPublicAngebotClient
    {
        public List<string> Tokens { get; } = [];

        public Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken)
        {
            Tokens.Add(token);
            return Task.FromResult(result);
        }
    }

    private static AngebotModel ModelFor(StubClient client, string token = "a-token") =>
        new(client)
        {
            Token = token,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task An_available_angebot_is_shown_with_200()
    {
        var client = new StubClient(CustomerAngebotResult.Available(new CustomerAngebot("ANG-2026-00042")));
        var model = ModelFor(client);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Available, model.Outcome);
        Assert.Equal("ANG-2026-00042", model.Angebot?.AngebotNumber);
        Assert.Equal(StatusCodes.Status200OK, model.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_link_answers_404_and_carries_no_angebot()
    {
        var model = ModelFor(new StubClient(CustomerAngebotResult.NotFound()));

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.NotFound, model.Outcome);
        Assert.Null(model.Angebot);
        Assert.Equal(StatusCodes.Status404NotFound, model.HttpContext.Response.StatusCode);
    }

    /// <summary>
    /// 410, matching the API's own answer for expiry and Sequence Diagram §12 — expiry is honestly
    /// distinguishable from absence, which costs nothing when the secret is 256 bits of CSPRNG
    /// output and spares a customer hunting for a mistyped URL.
    /// </summary>
    [Fact]
    public async Task An_expired_link_answers_410()
    {
        var model = ModelFor(new StubClient(CustomerAngebotResult.Expired()));

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Expired, model.Outcome);
        Assert.Equal(StatusCodes.Status410Gone, model.HttpContext.Response.StatusCode);
    }

    /// <summary>
    /// 503, never 404: an outage reported as a missing link tells the customer to give up on a link
    /// that is perfectly good.
    /// </summary>
    [Fact]
    public async Task An_outage_answers_503_not_404()
    {
        var model = ModelFor(new StubClient(CustomerAngebotResult.Unavailable()));

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(CustomerAngebotOutcome.Unavailable, model.Outcome);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, model.HttpContext.Response.StatusCode);
    }

    /// <summary>
    /// The route token reaches the client unaltered. The page neither trims, normalizes nor
    /// pre-validates it — deciding whether a token is real is the API's job, and a second opinion
    /// here would be a quieter, competing definition of what a token looks like.
    /// </summary>
    [Fact]
    public async Task The_route_token_is_passed_through_untouched()
    {
        var client = new StubClient(CustomerAngebotResult.NotFound());
        var model = ModelFor(client, "  odd/looking+token  ");

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal("  odd/looking+token  ", Assert.Single(client.Tokens));
    }
}
