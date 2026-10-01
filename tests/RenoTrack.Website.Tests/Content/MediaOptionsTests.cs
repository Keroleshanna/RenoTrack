using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The photo manifest, <c>Site:Media</c>, and the pages' references to it (<b>D106</b>). Shape and references only;
/// whether a photo is really approved is the private register's business.
/// </summary>
public sealed class MediaOptionsTests
{
    private const string SecretLookingValue = "geheim-kx7pq2zm";

    private static MediaItemOptions Item(
        string id = "testbild-eins",
        string? alt = "Ein Testbild (Testdaten)",
        string? caption = null,
        string? sourceRef = "test-register-0001") =>
        new() { Id = id, Alt = alt, Caption = caption, SourceRef = sourceRef };

    private static ServiceOptions Service(string slug = "eins", string? image = null) => new()
    {
        Slug = slug,
        Name = $"Testleistung {slug}",
        Summary = "Eine erfundene Leistung.",
        Offerings = ["Angebot"],
        MetaTitle = $"Titel {slug}",
        Image = image,
    };

    /// <summary>A disabled site: media are validated whenever supplied, so these rules need no identity.</summary>
    private static SiteOptions Site(IReadOnlyList<MediaItemOptions> media, string? heroImage = null, params ServiceOptions[] services) => new()
    {
        Media = media,
        Home = new HomePageOptions { HeroImage = heroImage },
        Services = services.Length == 0 ? [Service()] : services,
    };

    private static InvalidOperationException Refused(SiteOptions site) =>
        Assert.Throws<InvalidOperationException>(() => site.Validate(new CompanyIdentityOptions()));

    // ---- Items ---------------------------------------------------------------------------------

    [Fact]
    public void A_referenced_well_formed_item_is_valid()
    {
        Site([Item(caption: "Eine Bildunterschrift.")], heroImage: "testbild-eins").Validate(new CompanyIdentityOptions());
    }

    [Theory]
    [InlineData("Testbild")]
    [InlineData("test_bild")]
    [InlineData("test--bild")]
    [InlineData("-testbild")]
    [InlineData("../testbild")]
    [InlineData("testbild.jpg")]
    [InlineData("")]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijx")]
    public void An_id_that_is_not_lowercase_ascii_with_single_hyphens_is_refused(string id)
    {
        // No reference: the item's own shape is checked before any page's reference to it.
        var error = Refused(Site([Item(id)]));

        Assert.Contains("'Site:Media:0:Id'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Alt_text_is_required(string? alt)
    {
        var error = Refused(Site([Item(alt: alt)], heroImage: "testbild-eins"));

        Assert.Contains("'Site:Media:0:Alt'", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, MediaItemOptions> MalformedTextCases() => new()
    {
        { "Site:Media:0:Alt", Item(alt: new string('a', 151)) },
        { "Site:Media:0:Alt", Item(alt: "Zeile\nzwei") },
        { "Site:Media:0:Caption", Item(caption: new string('c', 201)) },
        { "Site:Media:0:Caption", Item(caption: " ") },
        { "Site:Media:0:Caption", Item(caption: "Zeile\tzwei") },
    };

    [Theory]
    [MemberData(nameof(MalformedTextCases))]
    public void Malformed_alt_or_caption_text_is_refused_naming_the_key(string key, MediaItemOptions item)
    {
        var error = Refused(Site([item], heroImage: "testbild-eins"));

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Texts_at_their_limits_are_accepted()
    {
        Site([Item(alt: new string('a', 150), caption: new string('c', 200))], heroImage: "testbild-eins").Validate(new CompanyIdentityOptions());
    }

    [Fact]
    public void A_caption_repeating_the_alt_text_is_refused()
    {
        var error = Refused(Site([Item(alt: "Ein Testbild", caption: " ein testbild ")], heroImage: "testbild-eins"));

        Assert.Contains("'Site:Media:0:Caption'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Register 1")]
    [InlineData("C:/Fotos/IMG_0001.jpg")]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefghijx")]
    public void A_source_reference_is_required_and_opaque(string? sourceRef)
    {
        var error = Refused(Site([Item(sourceRef: sourceRef)], heroImage: "testbild-eins"));

        Assert.Contains("'Site:Media:0:SourceRef'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_items_with_the_same_id_are_refused_naming_both_keys()
    {
        var error = Refused(Site([Item(), Item(sourceRef: "test-register-0002")], heroImage: "testbild-eins"));

        Assert.Contains("'Site:Media:0:Id'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Media:1:Id'", error.Message, StringComparison.Ordinal);
    }

    // ---- References ------------------------------------------------------------------------------

    [Fact]
    public void A_hero_image_naming_an_unlisted_photo_is_refused()
    {
        var error = Refused(Site([], heroImage: "testbild-fehlt"));

        Assert.Contains("'Site:Home:HeroImage'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_service_image_naming_an_unlisted_photo_is_refused()
    {
        var error = Refused(Site([], null, Service("eins"), Service("zwei", image: "testbild-fehlt")));

        Assert.Contains("'Site:Services:1:Image'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>S5-10: every listed photo is published, so one no page uses must not be listed.</summary>
    [Fact]
    public void A_listed_photo_no_page_uses_is_refused()
    {
        var error = Refused(Site([Item(), Item("testbild-zwei")], heroImage: "testbild-eins"));

        Assert.Contains("'Site:Media:1'", error.Message, StringComparison.Ordinal);
        Assert.Contains("not used by any page", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void One_photo_may_serve_the_hero_and_a_service()
    {
        Site([Item()], "testbild-eins", Service("eins", image: "testbild-eins")).Validate(new CompanyIdentityOptions());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_reference_is_refused_rather_than_ignored(string reference)
    {
        Assert.Contains("'Site:Home:HeroImage'", Refused(Site([], heroImage: reference)).Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services:0:Image'", Refused(Site([], null, Service(image: reference))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_refusal_repeats_the_configured_value()
    {
        var failures = new[]
        {
            Site([Item(alt: SecretLookingValue + new string('x', 150))], heroImage: "testbild-eins"),
            Site([], heroImage: SecretLookingValue),
            Site([Item(SecretLookingValue)]),
            Site([Item(sourceRef: SecretLookingValue + " x")], heroImage: "testbild-eins"),
        };

        foreach (var site in failures)
        {
            Assert.DoesNotContain(SecretLookingValue, Refused(site).Message, StringComparison.Ordinal);
        }
    }

    // ---- Through the real startup ------------------------------------------------------------------

    /// <summary>Lists merge entry by entry across sources, so the manifest must come from one source.</summary>
    [Theory]
    [InlineData("Site:Media:0:Alt")]
    [InlineData("Site:Media:5:Id")]
    public void A_media_entry_from_a_second_configuration_source_fails_startup(string key)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root, (key, "überschrieben"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'Site:Media'", error.Message, StringComparison.Ordinal);
    }
}
