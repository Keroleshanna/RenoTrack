using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// What the company may configure about itself, and which configurations refuse to start.
/// </summary>
/// <remarks>
/// Absent identity is the repository's real state and must stay valid; a logo that would reach a
/// third party, or that no screen reader can announce, is a mistake and is refused (<b>D100</b>).
/// </remarks>
public sealed class CompanyIdentityOptionsTests
{
    private static CompanyIdentityOptions WithLogo(string? logoPath, string? displayName = "Testfirma") =>
        new() { DisplayName = displayName, LogoPath = logoPath };

    // ---- Absence stays valid -----------------------------------------------

    [Fact]
    public void An_unconfigured_identity_is_valid_and_shows_nothing()
    {
        var options = new CompanyIdentityOptions();

        options.Validate();

        Assert.False(options.HasDisplayName);
        Assert.False(options.HasContactDetails);
        Assert.False(options.HasLogo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Whitespace_is_not_a_logo(string? logoPath)
    {
        Assert.False(WithLogo(logoPath).HasLogo);
        WithLogo(logoPath).Validate();
    }

    [Fact]
    public void Either_contact_detail_alone_counts()
    {
        Assert.True(new CompanyIdentityOptions { ContactEmail = "kontakt@example.test" }.HasContactDetails);
        Assert.True(new CompanyIdentityOptions { ContactPhone = "+49 000 0000000" }.HasContactDetails);
        Assert.False(new CompanyIdentityOptions { DisplayName = "Testfirma" }.HasContactDetails);
    }

    // ---- The logo must stay on this origin ---------------------------------

    /// <summary>
    /// Only the prefix that is actually served at run time. <c>/img/logo.svg</c> is site-relative
    /// and would still 404: <c>MapStaticAssets</c> serves only its build-time manifest, so a file
    /// added to <c>wwwroot</c> after publishing is on disk and unreachable. Found by serving one.
    /// </summary>
    [Theory]
    [InlineData("/img/logo.svg")]
    [InlineData("/logo.svg")]
    [InlineData("/wwwroot/logo.svg")]
    public void A_site_relative_logo_outside_the_served_prefix_is_refused(string logoPath)
    {
        var error = Assert.Throws<InvalidOperationException>(WithLogo(logoPath).Validate);

        Assert.Contains("/brand/", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/brand/logo.svg")]
    [InlineData("/brand/logo.png")]
    public void A_site_relative_logo_is_accepted(string logoPath)
    {
        WithLogo(logoPath).Validate();

        Assert.True(WithLogo(logoPath).HasLogo);
    }

    /// <summary>
    /// <b>The two that a naive <c>StartsWith('/')</c> check would let through.</b> Both begin with a
    /// slash and both leave this origin — the first is protocol-relative, and browsers normalise the
    /// backslash form to the same thing. This is why the guard is written out rather than assumed.
    /// </summary>
    [Theory]
    [InlineData("//cdn.example.test/logo.png")]
    [InlineData("/\\cdn.example.test/logo.png")]
    public void A_protocol_relative_logo_is_refused(string logoPath)
    {
        var error = Assert.Throws<InvalidOperationException>(WithLogo(logoPath).Validate);

        Assert.Contains("CompanyIdentity:LogoPath", error.Message, StringComparison.Ordinal);
        Assert.Contains("site-relative", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// HTTPS is not the point: any off-origin request discloses which customer opened which quote
    /// and when, to whoever serves it.
    /// </summary>
    [Theory]
    [InlineData("https://cdn.example.test/logo.png")]
    [InlineData("http://cdn.example.test/logo.png")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("javascript:alert(1)")]
    [InlineData("img/logo.svg")]
    public void An_off_origin_or_relative_logo_is_refused(string logoPath)
    {
        var error = Assert.Throws<InvalidOperationException>(WithLogo(logoPath).Validate);

        Assert.Contains("CompanyIdentity:LogoPath", error.Message, StringComparison.Ordinal);
    }

    // ---- A logo needs a name to announce it --------------------------------

    /// <summary>
    /// The logo replaces the company name in the header and uses it as alternative text, so a logo
    /// without a name would be an unlabelled image where the brand should be.
    /// </summary>
    [Fact]
    public void A_logo_without_a_display_name_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            WithLogo("/brand/logo.svg", displayName: null).Validate);

        Assert.Contains("CompanyIdentity:LogoPath", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyIdentity:DisplayName", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A name without a logo is entirely ordinary — that is today's state.</summary>
    [Fact]
    public void A_display_name_without_a_logo_is_valid()
    {
        new CompanyIdentityOptions { DisplayName = "Testfirma" }.Validate();
    }
}
