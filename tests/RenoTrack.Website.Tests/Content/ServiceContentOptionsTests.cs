using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The service-page content model: <c>Site:ServicesPage</c> and the fields Slice 4 adds to <c>Site:Services[]</c>
/// (<b>D105</b>). Shape only, never truth (<c>CLAUDE.md</c> §25).
/// </summary>
/// <remarks>Fictional values only — reserved <c>.test</c> domains (<b>D100</b>).</remarks>
public sealed class ServiceContentOptionsTests
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

    private static ServiceOptions Service(
        string slug = "eins",
        string? metaTitle = "Testleistung Eins in Testort",
        string? headline = null,
        string? metaDescription = null,
        IReadOnlyList<string>? offerings = null,
        IReadOnlyList<ServiceSectionOptions>? sections = null) => new()
    {
        Slug = slug,
        Name = $"Testleistung {slug}",
        Summary = "Eine erfundene Leistung.",
        Offerings = offerings ?? ["Erfundenes Angebot"],
        MetaTitle = metaTitle,
        Headline = headline,
        MetaDescription = metaDescription,
        Sections = sections ?? [],
    };

    private static ServiceSectionOptions Block(string? heading = "Abschnitt", params string[] paragraphs) =>
        new() { Heading = heading, Paragraphs = paragraphs.Length == 0 ? ["Ein Absatz."] : paragraphs };

    private static SiteOptions Enabled(
        IReadOnlyList<ServiceOptions>? services = null,
        ServicesPageOptions? servicesPage = null,
        HomePageOptions? home = null) => new()
    {
        PublicBaseUrl = Origin,
        Services = services ?? [Service()],
        Home = home ?? new HomePageOptions { MetaTitle = "Testleistungen in Testort" },
        ServicesPage = servicesPage ?? new ServicesPageOptions { MetaTitle = "Alle Testleistungen in Testort" },
    };

    private static SiteOptions Disabled(IReadOnlyList<ServiceOptions> services, ServicesPageOptions? servicesPage = null) =>
        new() { Services = services, ServicesPage = servicesPage ?? new ServicesPageOptions() };

    private static InvalidOperationException Refused(SiteOptions site) =>
        Assert.Throws<InvalidOperationException>(() => site.Validate(CompleteIdentity()));

    // ---- [S4-2] Required titles -------------------------------------------------------

    [Fact]
    public void A_complete_enabled_site_with_every_optional_field_is_valid()
    {
        Enabled(
            services: [Service(headline: "Überschrift", metaDescription: "Beschreibung.", sections: [Block(), Block("Zweiter", "Eins.", "Zwei.")])],
            servicesPage: new ServicesPageOptions { MetaTitle = "Übersicht", Headline = "Alle Leistungen", Intro = "Einleitung." })
            .Validate(CompleteIdentity());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_enabled_site_without_an_overview_title_is_refused_naming_the_key(string? metaTitle)
    {
        var error = Refused(Enabled(servicesPage: new ServicesPageOptions { MetaTitle = metaTitle }));

        Assert.Contains("'Site:ServicesPage:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enabled_site_with_a_service_without_a_title_is_refused_naming_that_service()
    {
        var error = Refused(Enabled(services: [Service("eins"), Service("zwei", metaTitle: null)]));

        Assert.Contains("'Site:Services:1:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'Site:Services:0:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Named in the one message with every other missing key, not discovered a restart later.</summary>
    [Fact]
    public void Missing_titles_are_named_together_with_the_other_missing_keys()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new SiteOptions { PublicBaseUrl = Origin, Services = [Service(metaTitle: null)] }.Validate(new CompanyIdentityOptions()));

        Assert.Contains("'Site:ServicesPage:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services:0:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Home:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:DisplayName'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disabled_site_requires_no_title()
    {
        Disabled([Service(metaTitle: null)]).Validate(CompleteIdentity());
    }

    // ---- Optional text: absent is fine, blank is not, and limits hold ----------------------

    public static TheoryData<string, SiteOptions> BlankOptionalValueCases() => new()
    {
        { "Site:ServicesPage:MetaTitle", Disabled([Service()], new ServicesPageOptions { MetaTitle = " " }) },
        { "Site:ServicesPage:Headline", Disabled([Service()], new ServicesPageOptions { Headline = "" }) },
        { "Site:ServicesPage:Intro", Disabled([Service()], new ServicesPageOptions { Intro = " " }) },
        { "Site:Services:0:MetaTitle", Disabled([Service(metaTitle: " ")]) },
        { "Site:Services:0:Headline", Disabled([Service(headline: " ")]) },
        { "Site:Services:0:MetaDescription", Disabled([Service(metaDescription: "")]) },
    };

    /// <summary>Refused on a disabled site as well: malformed content is never silently accepted.</summary>
    [Theory]
    [MemberData(nameof(BlankOptionalValueCases))]
    public void A_supplied_but_blank_optional_value_is_refused_naming_the_key(string key, SiteOptions site)
    {
        var error = Refused(site);

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, int, Func<string, SiteOptions>> LimitCases() => new()
    {
        { "Site:ServicesPage:MetaTitle", 70, value => Enabled(servicesPage: new ServicesPageOptions { MetaTitle = value }) },
        { "Site:ServicesPage:Headline", 90, value => Enabled(servicesPage: new ServicesPageOptions { MetaTitle = "T", Headline = value }) },
        { "Site:ServicesPage:Intro", 160, value => Enabled(servicesPage: new ServicesPageOptions { MetaTitle = "T", Intro = value }) },
        { "Site:Services:0:MetaTitle", 70, value => Enabled(services: [Service(metaTitle: value)]) },
        { "Site:Services:0:Headline", 90, value => Enabled(services: [Service(headline: value)]) },
        { "Site:Services:0:MetaDescription", 160, value => Enabled(services: [Service(metaDescription: value)]) },
        { "Site:Services:0:Sections:0:Heading", 80, value => Enabled(services: [Service(sections: [Block(value)])]) },
        { "Site:Services:0:Sections:0:Paragraphs:0", 600, value => Enabled(services: [Service(sections: [Block("H", value)])]) },
    };

    [Theory]
    [MemberData(nameof(LimitCases))]
    public void A_value_at_its_limit_is_accepted_and_one_over_is_refused_naming_the_key(string key, int limit, Func<string, SiteOptions> site)
    {
        site(new string('x', limit)).Validate(CompleteIdentity());

        var error = Refused(site(new string('x', limit + 1)));

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LimitCases))]
    public void A_control_character_is_refused_naming_the_key(string key, int limit, Func<string, SiteOptions> site)
    {
        Assert.True(limit > 10);

        var error = Refused(site("Zeile\nzwei"));

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    // ---- [S4-4] Offerings ------------------------------------------------------------------

    [Fact]
    public void Twelve_offerings_are_accepted_and_thirteen_are_refused()
    {
        Enabled(services: [Service(offerings: Enumerable.Range(1, 12).Select(index => $"Angebot {index}").ToList())]).Validate(CompleteIdentity());

        var error = Refused(Enabled(services: [Service(offerings: Enumerable.Range(1, 13).Select(index => $"Angebot {index}").ToList())]));

        Assert.Contains("'Site:Services:0:Offerings'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_offering_limit_applies_on_a_disabled_site_as_well()
    {
        var error = Refused(Disabled([Service(offerings: Enumerable.Range(1, 13).Select(index => $"Angebot {index}").ToList())]));

        Assert.Contains("'Site:Services:0:Offerings'", error.Message, StringComparison.Ordinal);
    }

    // ---- [S4-3] Sections --------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void None_to_four_sections_are_accepted(int count)
    {
        Enabled(services: [Service(sections: Enumerable.Range(1, count).Select(index => Block($"Abschnitt {index}")).ToList())]).Validate(CompleteIdentity());
    }

    [Fact]
    public void Five_sections_are_refused()
    {
        var error = Refused(Enabled(services: [Service(sections: Enumerable.Range(1, 5).Select(index => Block($"Abschnitt {index}")).ToList())]));

        Assert.Contains("'Site:Services:0:Sections'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_section_without_a_heading_is_refused_naming_the_key(string? heading)
    {
        var error = Refused(Enabled(services: [Service(sections: [Block(), Block(heading)])]));

        Assert.Contains("'Site:Services:0:Sections:1:Heading'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void One_to_four_paragraphs_are_accepted(int count)
    {
        var paragraphs = Enumerable.Range(1, count).Select(index => $"Absatz {index}.").ToList();

        Enabled(services: [Service(sections: [new ServiceSectionOptions { Heading = "H", Paragraphs = paragraphs }])]).Validate(CompleteIdentity());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void No_paragraphs_or_more_than_four_are_refused(int count)
    {
        var paragraphs = Enumerable.Range(1, count).Select(index => $"Absatz {index}.").ToList();

        var error = Refused(Enabled(services: [Service(sections: [new ServiceSectionOptions { Heading = "H", Paragraphs = paragraphs }])]));

        Assert.Contains("'Site:Services:0:Sections:0:Paragraphs'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_paragraph_is_refused_naming_its_key()
    {
        var error = Refused(Disabled([Service(sections: [Block("H", "Erster Absatz.", " ")])]));

        Assert.Contains("'Site:Services:0:Sections:0:Paragraphs:1'", error.Message, StringComparison.Ordinal);
    }

    // ---- [S4-5] Unique titles and descriptions -----------------------------------------------

    public static TheoryData<string, string, SiteOptions> DuplicateTitleCases() => new()
    {
        {
            "'Site:Home:MetaTitle'", "'Site:ServicesPage:MetaTitle'",
            Enabled(home: new HomePageOptions { MetaTitle = "Gleicher Titel" }, servicesPage: new ServicesPageOptions { MetaTitle = "gleicher titel" })
        },
        {
            "'Site:ServicesPage:MetaTitle'", "'Site:Services:0:MetaTitle'",
            Enabled(services: [Service(metaTitle: "Gleicher Titel ")], servicesPage: new ServicesPageOptions { MetaTitle = "Gleicher Titel" })
        },
        {
            "'Site:Services:0:MetaTitle'", "'Site:Services:1:MetaTitle'",
            Enabled(services: [Service("eins", metaTitle: "Gleicher Titel"), Service("zwei", metaTitle: "GLEICHER TITEL")])
        },
    };

    [Theory]
    [MemberData(nameof(DuplicateTitleCases))]
    public void Two_pages_with_the_same_title_ignoring_case_and_padding_are_refused_naming_both_keys(string first, string second, SiteOptions site)
    {
        var error = Refused(site);

        Assert.Contains(first, error.Message, StringComparison.Ordinal);
        Assert.Contains(second, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_pages_with_the_same_description_are_refused_naming_both_keys()
    {
        var error = Refused(Enabled(
            home: new HomePageOptions { MetaTitle = "Start", Subheadline = "Gleiche Beschreibung." },
            services: [Service(metaDescription: "gleiche beschreibung.")]));

        Assert.Contains("'Site:Home:Subheadline'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services:0:MetaDescription'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Absent descriptions are not "the same description": only supplied values are compared.</summary>
    [Fact]
    public void Pages_without_a_description_do_not_collide()
    {
        Enabled(services: [Service("eins", metaTitle: "Eins"), Service("zwei", metaTitle: "Zwei"), Service("drei", metaTitle: "Drei")]).Validate(CompleteIdentity());
    }

    [Fact]
    public void Uniqueness_is_checked_on_a_disabled_site_as_well()
    {
        var error = Refused(Disabled([Service("eins", metaTitle: "Gleich"), Service("zwei", metaTitle: "Gleich")]));

        Assert.Contains("'Site:Services:1:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    // ---- Messages name keys, never values ------------------------------------------------------

    [Fact]
    public void No_refusal_repeats_the_configured_value()
    {
        var failures = new Func<SiteOptions>[]
        {
            () => Enabled(services: [Service(metaTitle: SecretLookingValue + new string('x', 70))]),
            () => Enabled(services: [Service("eins", metaTitle: SecretLookingValue), Service("zwei", metaTitle: SecretLookingValue)]),
            () => Enabled(services: [Service(metaDescription: SecretLookingValue)], home: new HomePageOptions { MetaTitle = "T", Subheadline = SecretLookingValue }),
            () => Enabled(services: [Service(sections: [Block(SecretLookingValue + "\t")])]),
        };

        foreach (var failure in failures)
        {
            Assert.DoesNotContain(SecretLookingValue, Refused(failure()).Message, StringComparison.Ordinal);
        }
    }

    // ---- Through the real startup ---------------------------------------------------------------

    [Fact]
    public void An_enabled_pack_without_service_titles_fails_startup_naming_the_keys()
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
                "Home": { "MetaTitle": "Testleistungen in Testort (Testdaten)" },
                "Services": [ { "Slug": "eins", "Name": "Eins", "Summary": "Zusammenfassung.", "Offerings": [ "A" ] } ]
              }
            }
            """);
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:ServicesPage:MetaTitle'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services:0:MetaTitle'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The nested lists live under <c>Site:Services</c>, so the existing single-source rule covers them — verified
    /// here rather than assumed, because Slice 3 assumed the same of its lists and was wrong (D104).
    /// </summary>
    [Theory]
    [InlineData("Site:Services:0:Sections:0:Heading")]
    [InlineData("Site:Services:0:Sections:0:Paragraphs:0")]
    [InlineData("Site:Services:0:Offerings:0")]
    public void A_nested_service_list_entry_from_a_second_source_fails_startup(string key)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root, (key, "Überschrieben"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
    }
}
