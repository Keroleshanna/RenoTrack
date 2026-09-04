using System.Net;
using RenoTrack.Website.PublicApi;
using RenoTrack.Website.Security;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The customer's page as it is actually served: the route the emailed link points at, the headers
/// a page whose URL is a credential must carry, and what the four states put on screen.
/// </summary>
/// <remarks>
/// No database and no API process, so this runs on any operating system — including CI's Linux job,
/// which the LocalDB-backed suites cannot use (D56). That is deliberate: the customer-facing surface
/// is the part of this phase that most needs to stay verifiable everywhere.
/// </remarks>
public sealed class AngebotPageTests : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private readonly CustomerWebsiteFactory factory;

    public AngebotPageTests(CustomerWebsiteFactory factory)
    {
        this.factory = factory;
        factory.RequestedTokens.Clear();
        factory.Result = CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical());
    }

    // ---- The route ---------------------------------------------------------

    /// <summary>
    /// The path <c>EmailMessageFactory.AngebotUrl</c> has been composing since Phase 6 (D4.1). Until
    /// this slice it resolved to nothing; the email was already sending customers here.
    /// </summary>
    [Fact]
    public async Task The_emailed_route_resolves_and_the_token_reaches_the_api()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Token, Assert.Single(factory.RequestedTokens));
    }

    /// <summary>Possession of the link is the entire authorisation model (Architecture.md §7.2).</summary>
    [Fact]
    public async Task The_page_requires_no_authentication_of_any_kind()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A bare <c>/angebot</c> with no token matches no route rather than reaching the page.</summary>
    [Fact]
    public async Task The_route_without_a_token_is_not_the_angebot_page()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/angebot");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.RequestedTokens);
    }

    // ---- States ------------------------------------------------------------

    [Fact]
    public async Task An_available_angebot_shows_its_number()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ANG-2026-00042", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CustomerAngebotOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(CustomerAngebotOutcome.Expired, HttpStatusCode.Gone)]
    [InlineData(CustomerAngebotOutcome.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Each_failure_state_renders_its_own_page_with_its_own_status(
        CustomerAngebotOutcome outcome, HttpStatusCode expected)
    {
        factory.Result = new CustomerAngebotResult(outcome, null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);

        // A real page, not the framework's status-code body: the customer gets an explanation.
        Assert.Contains("customer-card", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// An outage must read differently from an invalid link. Conflating them would tell a customer
    /// to abandon a link that is perfectly good.
    /// </summary>
    [Fact]
    public async Task An_outage_does_not_tell_the_customer_their_link_is_invalid()
    {
        factory.Result = CustomerAngebotResult.Unavailable();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("nicht abrufbar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("nicht gültig", html, StringComparison.Ordinal);
    }

    // ---- Token handling ----------------------------------------------------

    /// <summary>
    /// The credential must not survive in the page: not in a link, not in a form, not in a hidden
    /// field, not in a heading. Anything rendered can be copied out of a shared screen, saved by a
    /// browser, or captured by page telemetry.
    /// </summary>
    [Theory]
    [InlineData(CustomerAngebotOutcome.Available)]
    [InlineData(CustomerAngebotOutcome.NotFound)]
    [InlineData(CustomerAngebotOutcome.Expired)]
    [InlineData(CustomerAngebotOutcome.Unavailable)]
    public async Task The_token_is_never_rendered_into_the_page(CustomerAngebotOutcome outcome)
    {
        factory.Result = outcome == CustomerAngebotOutcome.Available
            ? CustomerAngebotResult.Available(CustomerAngebotBuilder.Typical())
            : new CustomerAngebotResult(outcome, null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Token, html, StringComparison.Ordinal);
    }

    // ---- Security headers --------------------------------------------------

    [Fact]
    public async Task A_token_route_is_never_stored_by_a_cache_and_never_indexed()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
        Assert.Contains("noindex", Header(response, "X-Robots-Tag"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Without this, every outbound click hands the full token URL to whatever the customer clicked
    /// — which on a page whose URL is the credential is a disclosure, not a privacy nicety.
    /// </summary>
    [Fact]
    public async Task No_referrer_is_ever_sent_from_a_customer_page()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task The_baseline_headers_are_present()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    /// <summary>
    /// The strict rules are keyed on a route parameter named <c>token</c>, exactly as
    /// <c>RouteDiagnostics</c> keys the API's redaction — so a page with no credential in its URL is
    /// left cacheable, and a future <c>{token}/entscheidung</c> route is covered without anyone
    /// updating a list of paths.
    /// </summary>
    [Fact]
    public async Task A_page_without_a_token_in_its_url_is_not_forced_no_store()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Empty(Header(response, "X-Robots-Tag"));
        Assert.DoesNotContain("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);

        // The baseline still applies everywhere.
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public void The_token_route_rule_is_keyed_on_the_route_parameter_name()
    {
        // Pinned so the page's route template and the header rule cannot drift apart: renaming the
        // parameter in Angebot.cshtml would silently stop the strict headers being applied.
        Assert.Equal("token", CustomerSecurityHeaders.TokenRouteParameterName);
    }

    // ---- Customer surface --------------------------------------------------

    /// <summary>
    /// German only (Q8), and no way into the rest of the site: PermissionMatrix.md §7 grants a token
    /// holder exactly view and decide, so any other destination on screen would imply access they
    /// do not have — and every link is a chance to lose the page they were sent to.
    /// </summary>
    [Fact]
    public async Task The_customer_page_is_german_and_carries_no_internal_navigation()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"de\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("navbar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Privacy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("anmelden", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cockpit", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No company name is invented (Q7). With none configured the heading is omitted rather than
    /// filled with a placeholder a customer might read as real.
    /// </summary>
    [Fact]
    public async Task No_company_identity_is_invented_when_none_is_configured()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("customer-brand", html, StringComparison.Ordinal);
        Assert.DoesNotContain("RenoTrack.Website", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The flow is server-rendered end to end (D97). No script runs beside a page whose URL is a
    /// credential, and the API's origin is never disclosed to the customer's browser.
    /// </summary>
    [Fact]
    public async Task The_customer_page_loads_no_script_and_names_no_api_origin()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.example.test", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One header as a single string, empty when absent.
    /// </summary>
    /// <remarks>
    /// Joined rather than asserted per value: <c>HttpClient</c> parses headers it knows — notably
    /// <c>Cache-Control</c> — and may hand back its directives as separate values, so a
    /// value-count assertion would be testing the client's parsing rather than what the Website
    /// sent. Content headers are checked as a fallback because ASP.NET emits some headers there.
    /// </remarks>
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
