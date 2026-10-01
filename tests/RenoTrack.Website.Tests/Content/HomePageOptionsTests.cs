using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The homepage content model, <c>Site:Home</c> (<b>D104</b>). Shape only, never truth (<c>CLAUDE.md</c> §25).
/// </summary>
/// <remarks>Fictional values only — reserved <c>.test</c> domains (<b>D100</b>).</remarks>
public sealed class HomePageOptionsTests
{
    private const string Origin = "https://www.example.test";
    private const string SecretLookingValue = "Wert-Kx7Pq2Zm";

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

    private static ServiceOptions Service() => new()
    {
        Slug = "test-leistung", Name = "Testleistung", Summary = "Eine erfundene Leistung.", Offerings = ["Angebot"],
        MetaTitle = "Testleistung in Testort",
    };

    private static HomeItemOptions Item(string title = "Titel", string text = "Text.") => new() { Title = title, Text = text };

    /// <summary>An enabled site complete apart from the homepage under test; Slice 4 made the other titles required (D105).</summary>
    private static SiteOptions Enabled(HomePageOptions home) => new()
    {
        PublicBaseUrl = Origin,
        Services = [Service()],
        Home = home,
        ServicesPage = new ServicesPageOptions { MetaTitle = "Alle Testleistungen in Testort" },
    };

    private static InvalidOperationException Refused(SiteOptions site) =>
        Assert.Throws<InvalidOperationException>(() => site.Validate(CompleteIdentity()));

    // ---- [C1] MetaTitle ---------------------------------------------------------

    [Fact]
    public void An_enabled_site_with_a_meta_title_is_valid()
    {
        Enabled(new HomePageOptions { MetaTitle = "Testleistungen in Testort" }).Validate(CompleteIdentity());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_enabled_site_without_a_meta_title_is_refused_naming_the_key(string? metaTitle)
    {
        var error = Refused(Enabled(new HomePageOptions { MetaTitle = metaTitle }));

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Named in the one message with every other missing key, not discovered a restart later.</summary>
    [Fact]
    public void A_missing_meta_title_is_named_together_with_the_other_missing_keys()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new SiteOptions { PublicBaseUrl = Origin }.Validate(new CompanyIdentityOptions()));

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:DisplayName'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_site_does_not_require_a_meta_title()
    {
        new SiteOptions { Services = [Service()] }.Validate(new CompanyIdentityOptions());
    }

    [Fact]
    public void A_meta_title_at_the_limit_is_accepted_and_one_over_is_refused()
    {
        Enabled(new HomePageOptions { MetaTitle = new string('t', 70) }).Validate(CompleteIdentity());

        var error = Refused(Enabled(new HomePageOptions { MetaTitle = new string('t', 71) }));

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Zeile\nzwei")]
    [InlineData("Zeile\rzwei")]
    [InlineData("Zeile\tzwei")]
    [InlineData("Glocke")]
    public void A_meta_title_with_a_control_character_is_refused_without_echoing_the_value(string suffix)
    {
        var error = Refused(Enabled(new HomePageOptions { MetaTitle = SecretLookingValue + suffix }));

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretLookingValue, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_too_long_meta_title_is_refused_without_echoing_the_value()
    {
        var error = Refused(Enabled(new HomePageOptions { MetaTitle = SecretLookingValue + new string('x', 70) }));

        Assert.DoesNotContain(SecretLookingValue, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Malformed content is refused even when the site is disabled.</summary>
    [Fact]
    public void A_malformed_meta_title_is_refused_on_a_disabled_site_as_well()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new SiteOptions { Home = new HomePageOptions { MetaTitle = "a\nb" } }.Validate(new CompanyIdentityOptions()));

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    // ---- Headline and subheadline ---------------------------------------------------

    public static TheoryData<string, HomePageOptions> MalformedTextCases() => new()
    {
        { "Site:Home:Headline", new() { MetaTitle = "T", Headline = new string('h', 91) } },
        { "Site:Home:Headline", new() { MetaTitle = "T", Headline = " " } },
        { "Site:Home:Headline", new() { MetaTitle = "T", Headline = "a\nb" } },
        { "Site:Home:Subheadline", new() { MetaTitle = "T", Subheadline = new string('s', 161) } },
        { "Site:Home:Subheadline", new() { MetaTitle = "T", Subheadline = "" } },
        { "Site:Home:Subheadline", new() { MetaTitle = "T", Subheadline = "a\tb" } },
    };

    [Theory]
    [MemberData(nameof(MalformedTextCases))]
    public void A_malformed_headline_or_subheadline_is_refused_naming_the_key(string key, HomePageOptions home)
    {
        var error = Refused(Enabled(home));

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Headline_and_subheadline_at_their_limits_are_accepted()
    {
        Enabled(new HomePageOptions
        {
            MetaTitle = "T", Headline = new string('h', 90), Subheadline = new string('s', 160),
        }).Validate(CompleteIdentity());
    }

    // ---- Advantages and process -------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    public void None_or_two_to_six_entries_are_accepted(int count)
    {
        var items = Enumerable.Range(0, count).Select(_ => Item()).ToList();

        Enabled(new HomePageOptions { MetaTitle = "T", Advantages = items, Process = items }).Validate(CompleteIdentity());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void One_or_more_than_six_advantages_are_refused(int count)
    {
        var error = Refused(Enabled(new HomePageOptions
        {
            MetaTitle = "T", Advantages = Enumerable.Range(0, count).Select(_ => Item()).ToList(),
        }));

        Assert.Contains("'Site:Home:Advantages'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void One_or_more_than_six_process_steps_are_refused(int count)
    {
        var error = Refused(Enabled(new HomePageOptions
        {
            MetaTitle = "T", Process = Enumerable.Range(0, count).Select(_ => Item()).ToList(),
        }));

        Assert.Contains("'Site:Home:Process'", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, HomeItemOptions> MalformedItemCases() => new()
    {
        { "Title", new() { Title = null, Text = "Text." } },
        { "Title", new() { Title = " ", Text = "Text." } },
        { "Title", new() { Title = new string('t', 61), Text = "Text." } },
        { "Title", new() { Title = "a\nb", Text = "Text." } },
        { "Text", new() { Title = "Titel", Text = null } },
        { "Text", new() { Title = "Titel", Text = new string('x', 201) } },
        { "Text", new() { Title = "Titel", Text = "a\rb" } },
    };

    [Theory]
    [MemberData(nameof(MalformedItemCases))]
    public void A_malformed_entry_is_refused_naming_list_index_and_field(string field, HomeItemOptions item)
    {
        var advantages = Refused(Enabled(new HomePageOptions { MetaTitle = "T", Advantages = [Item(), item] }));
        var process = Refused(Enabled(new HomePageOptions { MetaTitle = "T", Process = [Item(), item] }));

        Assert.Contains($"'Site:Home:Advantages:1:{field}'", advantages.Message, StringComparison.Ordinal);
        Assert.Contains($"'Site:Home:Process:1:{field}'", process.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_at_their_limits_are_accepted()
    {
        var item = Item(new string('t', 60), new string('x', 200));

        Enabled(new HomePageOptions { MetaTitle = "T", Advantages = [item, item] }).Validate(CompleteIdentity());
    }

    // ---- Through the real startup ------------------------------------------------------

    [Fact]
    public void An_enabled_pack_without_a_meta_title_fails_startup_naming_the_key()
    {
        using var pack = TemporaryContentPack.With("""
            {
              "CompanyIdentity": {
                "DisplayName": "Testfirma (Testdaten)",
                "ContactPhone": "+49 000 0000000",
                "ContactEmail": "kontakt@example.test",
                "Address": { "StreetAddress": "Teststraße 1", "PostalCode": "00000", "Locality": "Testort", "CountryCode": "DE" }
              },
              "Site": {
                "PublicBaseUrl": "https://www.example.test",
                "Services": [ { "Slug": "eins", "Name": "Eins", "Summary": "Zusammenfassung.", "Offerings": [ "A" ] } ]
              }
            }
            """);
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Lists merge entry by entry across sources, so each homepage list must come from one source.</summary>
    [Theory]
    [InlineData("Site:Home:Advantages:0:Title", "Site:Home:Advantages")]
    [InlineData("Site:Home:Process:0:Title", "Site:Home:Process")]
    public void A_homepage_list_supplied_by_the_pack_and_another_source_fails_startup(string key, string section)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root, (key, "Überschrieben"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
    }
}
