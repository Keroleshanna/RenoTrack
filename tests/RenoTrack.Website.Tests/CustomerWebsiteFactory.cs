using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Boots the real RenoTrack.Website in-process — the actual <c>Program.cs</c>, not an approximation
/// — so these tests exercise the genuine pipeline: routing, the security headers, the layout, and
/// the page's own status codes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The API is stubbed at the <see cref="IPublicAngebotClient"/> boundary, deliberately.</b> The
/// alternative — booting the real API too — would drag SQL Server LocalDB into this suite and
/// confine it to CI's Windows job (D40, D56). The HTTP mapping this stub stands in for is covered
/// exhaustively by <c>PublicAngebotClientTests</c> against a stub transport, so nothing is left
/// untested; what is gained is that the Website's own surface stays verifiable on any OS, which is
/// the point of keeping it free of a database.
/// </para>
/// <para>
/// Configuration is supplied here rather than read from <c>appsettings.Development.json</c>, which
/// is gitignored — depending on it would make these tests pass locally and fail in a fresh clone.
/// </para>
/// </remarks>
public sealed class CustomerWebsiteFactory : WebApplicationFactory<Program>
{
    /// <summary>What the stubbed API returns. Set per test before the first request.</summary>
    public CustomerAngebotResult Result { get; set; } =
        CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical());

    /// <summary>Every token the Website asked the API about.</summary>
    public List<string> RequestedTokens { get; } = [];

    /// <summary>What the stubbed decision endpoint returns.</summary>
    public CustomerDecisionOutcome DecisionOutcome { get; set; } = CustomerDecisionOutcome.Recorded;

    /// <summary>
    /// Every decision the Website tried to record. <b>Its emptiness is the assertion</b> that the
    /// confirmation step records nothing — the property the whole two-step design exists for.
    /// </summary>
    public List<(string Token, CustomerDecisionChoice Choice)> RecordedDecisions { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Never Development: the customer surface must be tested as it will actually be served, and
        // Development would swap the exception page for the /Error handler under test.
        builder.UseEnvironment("Production");

        // Required, and validated as absolute HTTPS. Never reached — the client is replaced below —
        // but startup refuses without it, which is itself the behaviour PublicApiOptionsTests pins.
        builder.UseSetting(
            $"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}",
            "https://api.example.test");

        builder.ConfigureServices(services =>
        {
            // Removes the typed HttpClient registration and everything AddHttpClient layered on it,
            // so no test can accidentally reach a real socket.
            services.RemoveAll<IPublicAngebotClient>();
            services.AddSingleton<IPublicAngebotClient>(new StubPublicAngebotClient(this));
        });
    }

    private sealed class StubPublicAngebotClient(CustomerWebsiteFactory owner) : IPublicAngebotClient
    {
        public Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken)
        {
            owner.RequestedTokens.Add(token);
            return Task.FromResult(owner.Result);
        }

        public Task<CustomerDecisionOutcome> RecordDecisionAsync(
            string token,
            CustomerDecisionChoice choice,
            CancellationToken cancellationToken)
        {
            owner.RecordedDecisions.Add((token, choice));
            return Task.FromResult(owner.DecisionOutcome);
        }
    }
}
