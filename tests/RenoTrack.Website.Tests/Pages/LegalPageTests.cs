using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// The two legally required pages as they are actually served: their canonical routes, what happens
/// before the company has written a word, and the properties that keep them customer-safe.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unconfigured case is the repository's real state</b>, not a corner case — no legal text
/// exists here and none may be invented (Phase 11 Q7). So the 404 half of this file is testing what
/// the site does today, and the configured half is testing what it will do once the company
/// supplies its content (<b>D100</b> Part 1).
/// </para>
/// <para>
/// No database and no API process, so this stays in CI's Linux job with the rest of the customer
/// surface (D56).
/// </para>
/// </remarks>
public sealed class LegalPageTests(CustomerWebsiteFactory factory) : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    /// <summary>A host with the given legal configuration. The shared fixture keeps none.</summary>
    private WebApplicationFactory<Program> With(params Dictionary<string, string?>[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings.SelectMany(pairs => pairs))
            {
                builder.UseSetting(key, value);
            }
        });

    // ---- Absence means the route does not exist ----------------------------

    /// <summary>
    /// <b>The rule D100 Part 1 exists for.</b> Not an empty page, not a placeholder, not a
    /// "coming soon" — nothing at all, because a page that looks like a legal page and says nothing
    /// is worse than a 404 and is the variant that screenshots as correct.
    /// </summary>
    [Theory]
    [InlineData("/impressum")]
    [InlineData("/datenschutz")]
    public async Task An_unwritten_legal_page_is_404_and_not_an_empty_page(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A link to a 404 is exactly the dead end the 404 rule exists to avoid.</summary>
    [Fact]
    public async Task No_legal_link_is_rendered_while_the_pages_are_unwritten()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.DoesNotContain("/impressum", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/datenschutz", html, StringComparison.Ordinal);
    }

    /// <summary>One document may be written before the other, and each gates its own route.</summary>
    [Fact]
    public async Task Each_document_gates_its_own_route_independently()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum));
        using var client = host.CreateClient();

        using var impressum = await client.GetAsync("/impressum");
        using var datenschutz = await client.GetAsync("/datenschutz");

        Assert.Equal(HttpStatusCode.OK, impressum.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, datenschutz.StatusCode);

        var html = await client.GetStringAsync($"/angebot/{Token}");
        Assert.Contains("/impressum", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/datenschutz", html, StringComparison.Ordinal);
    }

    // ---- The canonical routes (OQ-5) ---------------------------------------

    [Theory]
    [InlineData("/impressum", LegalContent.Impressum)]
    [InlineData("/datenschutz", LegalContent.Datenschutz)]
    public async Task The_canonical_route_is_lowercase_and_serves_the_configured_document(string path, string document)
    {
        using var host = With(LegalContent.Document(document));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LegalContent.GermanTextRendered, html, StringComparison.Ordinal);
        Assert.Contains(LegalContent.Heading, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Razor Pages matches case-insensitively, which is a convenience and not the contract. The
    /// canonical form is the lowercase one; this pins that the capitalised spelling still resolves
    /// rather than 404-ing a customer who retyped the URL — the same hazard Slice 4 hit with
    /// <c>/entscheidung/Annehmen</c>.
    /// </summary>
    [Theory]
    [InlineData("/Impressum")]
    [InlineData("/IMPRESSUM")]
    [InlineData("/Datenschutz")]
    public async Task A_differently_cased_url_still_reaches_the_page(string path)
    {
        using var host = With(
            LegalContent.Document(LegalContent.Impressum),
            LegalContent.Document(LegalContent.Datenschutz));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Customer-safe, like every other page on this site ------------------

    /// <summary>
    /// <c>CLAUDE.md</c> §24: no script element on a customer page at all. These pages use the
    /// customer layout, so they inherit that — which is exactly what the legacy <c>_Layout</c>
    /// would have broken had they been left on it.
    /// </summary>
    [Fact]
    public async Task A_legal_page_carries_no_script_element()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync("/impressum");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>German only (Q8), and rendered as German rather than as numeric references.</summary>
    [Fact]
    public async Task German_characters_survive_the_encoder()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync("/impressum");

        Assert.Contains("ü", html, StringComparison.Ordinal);
        Assert.Contains("²", html, StringComparison.Ordinal);
        Assert.Contains("€", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&#xFC;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x20AC;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Operator-supplied text is still rendered through Razor's encoding expressions — there is no
    /// <c>Html.Raw</c> anywhere on a customer page (<c>CLAUDE.md</c> §24, <b>D100</b> Part 2), and
    /// this proves it rather than trusting the review that wrote it.
    /// </summary>
    [Fact]
    public async Task Markup_in_configured_text_is_rendered_inert()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum, "<script>alert(1)</script>"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync("/impressum");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_configured_link_is_rendered_as_a_link()
    {
        using var host = With(LegalContent.DocumentWithLink(
            LegalContent.Impressum, "https://example.test/odr", "Streitbeilegung"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync("/impressum");

        Assert.Contains("href=\"https://example.test/odr\"", html, StringComparison.Ordinal);
        Assert.Contains(">Streitbeilegung</a>", html, StringComparison.Ordinal);
    }

    // ---- Security headers: baseline yes, credential rules no ----------------

    /// <summary>
    /// A legal route has no <c>token</c> route parameter, so <c>CustomerSecurityHeaders</c> gives it
    /// the baseline and withholds the credential-page rules. That is the intended consequence of
    /// keying those rules on a route parameter rather than a path list — a public legal page
    /// *should* be cacheable and indexable — so it is pinned rather than assumed.
    /// </summary>
    [Fact]
    public async Task A_legal_page_gets_the_baseline_headers_and_not_the_token_page_rules()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum));
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/impressum");

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Null(response.Headers.CacheControl?.NoStore);
    }

    // ---- The quote page keeps every property it had -------------------------

    /// <summary>
    /// <b>The reason the footer links are safe.</b> They carry no token, so <c>TokenExposure</c>'s
    /// narrowed rule — the credential may appear only inside an <c>href</c> under
    /// <c>/angebot/</c> — still holds with them present. This is the assertion that would fail if a
    /// future change ever routed a legal page under the token.
    /// </summary>
    [Fact]
    public async Task The_legal_links_do_not_carry_the_token()
    {
        using var host = With(
            LegalContent.Document(LegalContent.Impressum),
            LegalContent.Document(LegalContent.Datenschutz));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.Contains("href=\"/impressum\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/datenschutz\"", html, StringComparison.Ordinal);
        TokenExposure.AssertOnlyInSameOriginLinks(html, Token);
    }

    /// <summary>
    /// The quote page's own headers are unchanged by the footer gaining links: it is still a
    /// credential page and must still refuse caching and indexing.
    /// </summary>
    [Fact]
    public async Task The_quote_page_keeps_its_credential_headers()
    {
        using var host = With(LegalContent.Document(LegalContent.Impressum));
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/angebot/{Token}");

        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")), StringComparison.Ordinal);
    }

    // ---- Startup refuses broken content ------------------------------------

    /// <summary>
    /// Malformed content fails startup naming the key, rather than reaching a customer as a link
    /// that executes script. Absent content, by contrast, starts fine — that difference is the whole
    /// of <c>LegalContentOptions.Validate</c>'s policy.
    /// </summary>
    [Fact]
    public void A_dangerous_configured_link_refuses_to_start()
    {
        using var host = With(LegalContent.DocumentWithLink(
            LegalContent.Impressum, "javascript:alert(1)", "Klick"));

        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("scheme", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
