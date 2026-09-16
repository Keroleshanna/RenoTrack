using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The content pack may contribute its own sections and nothing else (<b>D102</b>, Slice 1 decision
/// S1-8) — checked at the policy level, where both mechanisms can be exercised independently.
/// </summary>
/// <remarks>
/// <b>The two mechanisms are tested separately on purpose.</b> The validation turns a violation into a
/// startup failure; the filter guarantees an unexpected key cannot reach application configuration even
/// if that validation regressed. A test that only ever ran them together could not tell which one was
/// doing the work.
/// </remarks>
public sealed class ContentPackSectionPolicyTests
{
    private const string Site = ContentPackSectionPolicy.SiteFileName;
    private const string Legal = ContentPackSectionPolicy.LegalFileName;

    /// <summary>A value no message may ever contain.</summary>
    private const string SecretSentinel = "Server=db;Password=do-not-echo-3f9a";

    private static IReadOnlyList<string> SiteRoots => ContentPackSectionPolicy.AllowedRootsFor(Site);

    private static IReadOnlyList<string> LegalRoots => ContentPackSectionPolicy.AllowedRootsFor(Legal);

    private static InvalidOperationException Refusal(string fileName, params string[] keys) =>
        Assert.Throws<InvalidOperationException>(() => ContentPackSectionPolicy.EnsureOnlyAllowedRoots(
            $"'{fileName}' in 'pack'", fileName, keys, ContentPackSectionPolicy.AllowedRootsFor(fileName)));

    // ---- The allowed roots, exactly ----------------------------------------

    [Fact]
    public void The_manifest_may_contain_only_identity_and_site()
    {
        Assert.Equal(new[] { "CompanyIdentity", "Site" }, SiteRoots);
    }

    [Fact]
    public void The_legal_file_may_contain_only_legal()
    {
        Assert.Equal(new[] { "Legal" }, LegalRoots);
    }

    [Fact]
    public void Allowed_sections_with_content_are_accepted()
    {
        ContentPackSectionPolicy.EnsureOnlyAllowedRoots(
            "'site.json'", Site, ["CompanyIdentity:DisplayName", "Site:Services:0:Slug"], SiteRoots);
        ContentPackSectionPolicy.EnsureOnlyAllowedRoots(
            "'legal.json'", Legal, ["Legal:Impressum:Sections:0:Heading"], LegalRoots);
    }

    /// <summary>Configuration keys are case-insensitive, so a different casing is the same allowed section.</summary>
    [Fact]
    public void An_allowed_section_in_different_casing_is_the_same_section()
    {
        ContentPackSectionPolicy.EnsureOnlyAllowedRoots(
            "'site.json'", Site, ["companyidentity:DisplayName", "SITE:PublicBaseUrl"], SiteRoots);
    }

    // ---- Product and security configuration is refused ----------------------

    [Theory]
    [InlineData("ConnectionStrings")]
    [InlineData("Jwt")]
    [InlineData("Email")]
    [InlineData("TokenLink")]
    [InlineData("Logging")]
    [InlineData("PublicApi")]
    [InlineData("TrustedForwarders")]
    [InlineData("ContentPack")]
    [InlineData("AllowedHosts")]
    [InlineData("Kestrel")]
    [InlineData("RateLimiting")]
    [InlineData("Legal")]
    [InlineData("Foo")]
    [InlineData("//")]
    public void The_manifest_refuses_any_other_section_naming_the_file_and_the_section(string section)
    {
        var error = Refusal(Site, "CompanyIdentity:DisplayName", $"{section}:Anything");

        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CompanyIdentity")]
    [InlineData("Site")]
    [InlineData("Logging")]
    [InlineData("ConnectionStrings")]
    public void The_legal_file_refuses_any_other_section(string section)
    {
        var error = Refusal(Legal, "Legal:Impressum:Sections:0:Heading", $"{section}:Anything");

        Assert.Contains("'legal.json'", error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A top-level scalar such as <c>"AllowedHosts": "*"</c> is a section of its own and is refused too.</summary>
    [Fact]
    public void A_top_level_scalar_outside_the_allowed_roots_is_refused()
    {
        var error = Refusal(Site, "Site:PublicBaseUrl", "AllowedHosts");

        Assert.Contains("'AllowedHosts'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_unexpected_section_is_named_in_one_message()
    {
        var error = Refusal(Site, "Site:PublicBaseUrl", "Logging:LogLevel:Default", "Jwt:SigningKey", "PublicApi:BaseUrl");

        Assert.Contains("'Jwt'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Logging'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'PublicApi'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A JSON property literally named <c>"Logging:LogLevel:Default"</c> flattens to a <c>Logging</c> key.
    /// Judging flattened keys, not JSON shape, is what catches it.
    /// </summary>
    [Theory]
    [InlineData("Logging:LogLevel:Default")]
    [InlineData("Jwt:SigningKey")]
    [InlineData("PublicApi:BaseUrl")]
    public void A_colon_in_a_property_name_cannot_escape_to_another_section(string flattenedKey)
    {
        var error = Refusal(Site, "Site:PublicBaseUrl", flattenedKey);

        Assert.Contains($"'{flattenedKey.Split(':')[0]}'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A section name that merely starts with an allowed one is a different section.</summary>
    [Fact]
    public void A_section_that_only_shares_a_prefix_with_an_allowed_one_is_refused()
    {
        var error = Refusal(Site, "Site:PublicBaseUrl", "SiteSettings:Anything");

        Assert.Contains("'SiteSettings'", error.Message, StringComparison.Ordinal);
    }

    // ---- Allowed roots must be sections with content ------------------------

    [Fact]
    public void An_allowed_root_holding_a_scalar_is_refused()
    {
        var error = Refusal(Site, "Site", "CompanyIdentity:DisplayName");

        Assert.Contains("'Site'", error.Message, StringComparison.Ordinal);
        Assert.Contains("section with content", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_no_allowed_section_is_refused()
    {
        var error = Refusal(Site);

        Assert.Contains("contains no content", error.Message, StringComparison.Ordinal);
    }

    // ---- Messages never echo values -----------------------------------------

    /// <summary>
    /// The policy is given keys only, so a value cannot reach its message by construction — pinned here
    /// by passing the sentinel as a *key* segment below the refused root, the one place it could leak.
    /// </summary>
    [Fact]
    public void A_refusal_names_the_section_but_not_what_lies_beneath_it()
    {
        var error = Refusal(Site, "Site:PublicBaseUrl", $"ConnectionStrings:{SecretSentinel}");

        Assert.Contains("'ConnectionStrings'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unprintable_or_very_long_section_name_is_not_echoed_whole()
    {
        const char Bell = (char)7;
        var longName = new string('X', 500);
        var error = Refusal(Site, "Site:PublicBaseUrl", $"{longName}:Key", $"Bad{Bell}Name:Key");

        Assert.DoesNotContain(longName, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Bell, error.Message);
    }

    // ---- The filter, on its own ---------------------------------------------

    /// <summary>
    /// <b>The structural guarantee, without the validation in front of it.</b> Even given a key set the
    /// validation would have refused, the filter exposes nothing outside the allowed roots.
    /// </summary>
    [Fact]
    public void The_filter_alone_drops_every_key_outside_the_allowed_roots()
    {
        var raw = new Dictionary<string, string?>
        {
            ["CompanyIdentity:DisplayName"] = "Testfirma",
            ["site:PublicBaseUrl"] = "https://www.example.test",
            ["Logging:LogLevel:Default"] = "Trace",
            ["ConnectionStrings:RenoTrackDb"] = SecretSentinel,
            ["PublicApi:BaseUrl"] = "https://attacker.example.test",
            ["TrustedForwarders:KnownNetworks:0"] = "0.0.0.0/0",
            ["SiteSettings:Anything"] = "x",
            ["Site"] = "scalar",
            ["Legal:Impressum:Sections:0:Heading"] = "wrong file",
        };

        var filtered = ContentPackSectionPolicy.Filter(raw, SiteRoots);

        Assert.Equal(
            new[] { "CompanyIdentity:DisplayName", "site:PublicBaseUrl" },
            filtered.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(SecretSentinel, filtered.Values);
    }

    [Fact]
    public void The_filter_keeps_every_key_under_an_allowed_root()
    {
        var raw = new Dictionary<string, string?>
        {
            ["Legal:Impressum:Sections:0:Heading"] = "Überschrift",
            ["Legal:Datenschutz:Sections:0:Paragraphs:0:Text"] = "Text",
        };

        Assert.Equal(raw, ContentPackSectionPolicy.Filter(raw, LegalRoots));
    }
}
