using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Pages;

/// <summary>
/// What the customer actually sees of the company, set and unset.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unset half is the repository's real state</b> — no company name, contact detail or logo
/// exists here and none may be invented (Phase 11 Q7). The page must be plain rather than
/// placeholdered: a fabricated identity on a page a real customer reads is worse than a nameless
/// one, and it is the variant nobody notices before launch.
/// </para>
/// </remarks>
public sealed class CompanyIdentityRenderingTests(CustomerWebsiteFactory factory)
    : IClassFixture<CustomerWebsiteFactory>
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    /// <summary>Deliberately obviously fake — see <c>LegalContent</c> for why.</summary>
    private const string Name = "Test-Musterbetrieb (kein echter Firmenname)";

    private WebApplicationFactory<Program> With(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static (string, string) Setting(string key, string value) => ($"CompanyIdentity:{key}", value);

    // ---- Unset: plain, never placeholdered ---------------------------------

    [Fact]
    public async Task An_unconfigured_site_shows_no_company_name_and_invents_none()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.DoesNotContain("customer-brand", html, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-logo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Fragen zu Ihrem Angebot?", html, StringComparison.Ordinal);
    }

    /// <summary>The title carries no trailing separator when there is no name to follow it.</summary>
    [Fact]
    public async Task An_unconfigured_site_leaves_no_dangling_title_separator()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.DoesNotContain(" · </title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("·", html, StringComparison.Ordinal);
    }

    // ---- Set: name, contact, title -----------------------------------------

    [Fact]
    public async Task A_configured_name_reaches_the_header_and_the_title()
    {
        using var host = With(Setting("DisplayName", Name));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.Contains($"<p class=\"customer-brand\">{Name}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"· {Name}</title>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The leading "+" of a phone number is served as <c>&amp;#x2B;</c>, and that is correct.</b>
    /// .NET's <c>HtmlEncoder</c> escapes <c>+</c> unconditionally — it is in the always-escaped set,
    /// not governed by the allowed Unicode ranges <c>Program.cs</c> widens — so a configured
    /// <c>+49 …</c> appears in the source as an entity and renders to the reader as <c>+</c>.
    /// Asserted in its rendered form so nobody later "fixes" the encoder to make a raw match pass.
    /// This test was written expecting the raw string, failed, and the expectation was what was
    /// wrong (<c>CLAUDE.md</c> §14).
    /// </summary>
    [Fact]
    public async Task Configured_contact_details_reach_the_footer()
    {
        using var host = With(
            Setting("ContactEmail", "kontakt@example.test"),
            Setting("ContactPhone", "+49 000 0000000"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.Contains("Fragen zu Ihrem Angebot?", html, StringComparison.Ordinal);
        Assert.Contains("kontakt@example.test", html, StringComparison.Ordinal);
        Assert.Contains("&#x2B;49 000 0000000", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Company identity is staff-entered text on a page an anonymous link holder reads, so it goes
    /// through the same encoding boundary as everything else (<c>CLAUDE.md</c> §24).
    /// </summary>
    [Fact]
    public async Task Markup_in_a_configured_name_is_rendered_inert()
    {
        using var host = With(Setting("DisplayName", "<script>alert(1)</script>"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    // ---- The logo (Wireframe A3) -------------------------------------------

    [Fact]
    public async Task A_configured_logo_replaces_the_name_and_is_captioned_by_it()
    {
        using var host = With(Setting("DisplayName", Name), Setting("LogoPath", "/brand/logo.svg"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.Contains("<img class=\"customer-logo\" src=\"/brand/logo.svg\"", html, StringComparison.Ordinal);
        Assert.Contains($"alt=\"{Name}\"", html, StringComparison.Ordinal);

        // Replaces rather than accompanies: a header carrying both says the company twice to a
        // screen reader, once as text and once as the image's alternative text.
        Assert.DoesNotContain("customer-brand", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page still loads nothing off-origin, which is the property the startup guard exists to
    /// keep. Asserted on the rendered page too, because that is where a regression would show.
    /// </summary>
    [Fact]
    public async Task A_customer_page_requests_nothing_off_origin()
    {
        using var host = With(Setting("DisplayName", Name), Setting("LogoPath", "/brand/logo.svg"));
        using var client = host.CreateClient();

        var html = await client.GetStringAsync($"/angebot/{Token}");

        Assert.DoesNotContain("src=\"http", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"//", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"http", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"//", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Startup refuses an off-origin logo through the real host, not only in the options unit test.
    /// </summary>
    [Fact]
    public void An_off_origin_logo_refuses_to_start()
    {
        using var host = With(
            Setting("DisplayName", Name),
            Setting("LogoPath", "https://cdn.example.test/logo.png"));

        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("CompanyIdentity:LogoPath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_logo_without_a_name_refuses_to_start()
    {
        using var host = With(Setting("LogoPath", "/brand/logo.svg"));

        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());

        Assert.Contains("CompanyIdentity:DisplayName", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Identity is chrome, so it appears on the legal pages too, not only on the quote.</summary>
    [Fact]
    public async Task The_identity_appears_on_a_legal_page_as_well()
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("CompanyIdentity:DisplayName", Name);
            foreach (var (key, value) in LegalContent.Document(LegalContent.Impressum))
            {
                builder.UseSetting(key, value);
            }
        });
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/impressum");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Name, html, StringComparison.Ordinal);
    }
}
