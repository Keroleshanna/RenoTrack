using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The marketing site's switch, canonical origin and service catalog (<b>D102</b>).
/// </summary>
/// <remarks>Fictional values only — reserved <c>.test</c> domains (<b>D100</b>).</remarks>
public sealed class SiteOptionsTests
{
    private const string Origin = "https://www.example.test";

    private static CompanyIdentityOptions CompleteIdentity() => new()
    {
        DisplayName = "Testfirma",
        ContactPhone = "+49 000 0000000",
        ContactEmail = "kontakt@example.test",
        Address = new PostalAddressOptions
        {
            StreetAddress = "Teststraße 1", PostalCode = "00000", Locality = "Testort", CountryCode = "DE",
        },
    };

    private static ServiceOptions Service(string slug = "test-leistung", string name = "Testleistung") => new()
    {
        Slug = slug,
        Name = name,
        Summary = "Eine erfundene Leistung.",
        Offerings = ["Erfundenes Angebot"],
        MetaTitle = $"{name} in Testort",
    };

    /// <summary>The homepage title an enabled site requires (Slice 3, D104).</summary>
    private static HomePageOptions Home() => new() { MetaTitle = "Testleistungen in Testort" };

    /// <summary>The overview title an enabled site requires (Slice 4, D105).</summary>
    private static ServicesPageOptions ServicesPage() => new() { MetaTitle = "Alle Testleistungen in Testort" };

    private static InvalidOperationException Refused(SiteOptions site, CompanyIdentityOptions? identity = null) =>
        Assert.Throws<InvalidOperationException>(() => site.Validate(identity ?? CompleteIdentity()));

    // ---- The switch ----------------------------------------------------------

    [Fact]
    public void Without_a_public_base_url_the_site_is_disabled_and_nothing_is_required()
    {
        var site = new SiteOptions();

        site.Validate(new CompanyIdentityOptions());

        Assert.False(site.IsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_public_base_url_does_not_enable_the_site(string value)
    {
        Assert.False(new SiteOptions { PublicBaseUrl = value }.IsEnabled);
    }

    [Fact]
    public void A_complete_enabled_site_is_valid()
    {
        var site = new SiteOptions { PublicBaseUrl = Origin, Services = [Service()], Home = Home(), ServicesPage = ServicesPage() };

        site.Validate(CompleteIdentity());

        Assert.True(site.IsEnabled);
    }

    /// <summary>One message, every missing key — an operator should not discover them one restart at a time.</summary>
    [Fact]
    public void An_enabled_site_with_nothing_else_names_every_missing_key_in_one_message()
    {
        var error = Refused(new SiteOptions { PublicBaseUrl = Origin }, new CompanyIdentityOptions());

        Assert.Contains("'CompanyIdentity:DisplayName'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:ContactPhone'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:ContactEmail'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:Address'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_incomplete_identity_is_valid_when_the_site_is_disabled()
    {
        new SiteOptions { Services = [Service()] }.Validate(new CompanyIdentityOptions());
    }

    // ---- Canonical origin ----------------------------------------------------

    [Theory]
    [InlineData("https://www.example.test")]
    [InlineData("https://www.example.test/")]
    [InlineData("https://example.test:8443")]
    public void An_https_origin_is_accepted(string value)
    {
        new SiteOptions { PublicBaseUrl = value, Services = [Service()], Home = Home(), ServicesPage = ServicesPage() }.Validate(CompleteIdentity());
    }

    [Theory]
    [InlineData("http://www.example.test")]
    [InlineData("www.example.test")]
    [InlineData("/relative")]
    [InlineData("https://www.example.test/de")]
    [InlineData("https://www.example.test/de/")]
    [InlineData("https://www.example.test/?q=1")]
    [InlineData("https://www.example.test/?")]
    [InlineData("https://www.example.test/#top")]
    [InlineData("https://www.example.test#")]
    [InlineData("https://user:secret@www.example.test")]
    [InlineData("ftp://www.example.test")]
    public void Anything_but_an_https_origin_is_refused_naming_the_key(string value)
    {
        var error = Refused(new SiteOptions { PublicBaseUrl = value, Services = [Service()] });

        Assert.Contains("'Site:PublicBaseUrl'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://WWW.Example.TEST", "https://www.example.test")]
    [InlineData("https://www.example.test/", "https://www.example.test")]
    [InlineData("https://example.test:8443/", "https://example.test:8443")]
    public void The_canonical_origin_is_lower_cased_without_a_trailing_slash(string configured, string expected)
    {
        Assert.Equal(expected, new SiteOptions { PublicBaseUrl = configured }.CanonicalOrigin);
    }

    // ---- Services ------------------------------------------------------------

    [Theory]
    [InlineData("Fliesen")]
    [InlineData("türen")]
    [InlineData("-fliesen")]
    [InlineData("fliesen-")]
    [InlineData("flie--sen")]
    [InlineData("flie sen")]
    [InlineData("flie_sen")]
    [InlineData("")]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefghijx")]
    public void A_slug_that_is_not_lowercase_ascii_with_single_hyphens_is_refused(string slug)
    {
        var error = Refused(new SiteOptions { Services = [Service(slug)] });

        Assert.Contains("'Site:Services:0:Slug'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fliesen")]
    [InlineData("tueren")]
    [InlineData("innen-ausbau-2")]
    public void A_well_formed_slug_is_accepted(string slug)
    {
        new SiteOptions { Services = [Service(slug)] }.Validate(CompleteIdentity());
    }

    /// <summary>Validated even when the site is disabled: malformed content is never silently accepted.</summary>
    [Fact]
    public void A_malformed_service_is_refused_on_a_disabled_site_as_well()
    {
        Assert.False(new SiteOptions { Services = [Service("Bad Slug")] }.IsEnabled);

        Refused(new SiteOptions { Services = [Service("Bad Slug")] });
    }

    [Fact]
    public void Two_services_with_the_same_slug_are_refused()
    {
        var error = Refused(new SiteOptions { Services = [Service("gleich", "Eins"), Service("gleich", "Zwei")] });

        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'gleich'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_services_with_the_same_name_in_different_case_are_refused()
    {
        var error = Refused(new SiteOptions { Services = [Service("eins", "Testleistung"), Service("zwei", "TESTLEISTUNG")] });

        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, ServiceOptions> IncompleteServiceCases() => new()
    {
        { "Site:Services:0:Name", new() { Slug = "eins", Name = " ", Summary = "S", Offerings = ["A"] } },
        { "Site:Services:0:Summary", new() { Slug = "eins", Name = "N", Summary = null, Offerings = ["A"] } },
        { "Site:Services:0:Offerings", new() { Slug = "eins", Name = "N", Summary = "S", Offerings = [] } },
        { "Site:Services:0:Offerings:1", new() { Slug = "eins", Name = "N", Summary = "S", Offerings = ["A", " "] } },
        { "Site:Services:0:Name", new() { Slug = "eins", Name = new string('n', 61), Summary = "S", Offerings = ["A"] } },
        { "Site:Services:0:Summary", new() { Slug = "eins", Name = "N", Summary = new string('s', 301), Offerings = ["A"] } },
        { "Site:Services:0:Offerings:0", new() { Slug = "eins", Name = "N", Summary = "S", Offerings = [new string('a', 161)] } },
        { "Site:Services:0:Name", new() { Slug = "eins", Name = "Zeile\tzwei", Summary = "S", Offerings = ["A"] } },
        { "Site:Services:0:Summary", new() { Slug = "eins", Name = "N", Summary = "Zeile\nzwei", Offerings = ["A"] } },
        { "Site:Services:0:Offerings:0", new() { Slug = "eins", Name = "N", Summary = "S", Offerings = ["Zeile\rzwei"] } },
    };

    [Theory]
    [MemberData(nameof(IncompleteServiceCases))]
    public void An_incomplete_or_malformed_service_is_refused_naming_the_key(string key, ServiceOptions service)
    {
        var error = Refused(new SiteOptions { PublicBaseUrl = Origin, Services = [service] });

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }
}
