using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The identity fields Phase 13 added (<b>D102</b>) and the stricter contact formats that now apply to
/// every deployment (Slice 1 decision S1-3).
/// </summary>
/// <remarks>
/// Every value is fictional: reserved <c>.test</c> domains and <c>+49 000</c> numbers (<b>D100</b>).
/// </remarks>
public sealed class CompanyIdentityMarketingTests
{
    private static InvalidOperationException Refused(CompanyIdentityOptions options) =>
        Assert.Throws<InvalidOperationException>(options.Validate);

    private static PostalAddressOptions WholeAddress() => new()
    {
        StreetAddress = "Teststraße 1",
        PostalCode = "00000",
        Locality = "Testort",
        CountryCode = "DE",
    };

    // ---- Phone ---------------------------------------------------------------

    [Theory]
    [InlineData("+49 000 0000000")]
    [InlineData("+49 000 1111111")]
    [InlineData("+490001111111")]
    [InlineData("+43 1 23456789")]
    public void An_international_phone_number_is_accepted(string phone)
    {
        new CompanyIdentityOptions { ContactPhone = phone }.Validate();
    }

    [Theory]
    [InlineData("0000 1111111")]        // national form
    [InlineData("0049 000 1111111")]    // no '+'
    [InlineData("+0 000 1111111")]      // country code cannot start with 0
    [InlineData("+49  000 1111111")]    // double space
    [InlineData("+49-000-1111111")]     // other separators
    [InlineData("+49 (0) 000 1111111")]
    [InlineData("+49 000 11111a1")]
    [InlineData("+49 000")]             // too few digits
    [InlineData("+49 000 1111111111111")] // too many digits
    [InlineData(" +49 000 1111111")]    // leading whitespace
    public void Any_other_phone_form_is_refused_naming_the_key(string phone)
    {
        var error = Refused(new CompanyIdentityOptions { ContactPhone = phone });

        Assert.Contains("'CompanyIdentity:ContactPhone'", error.Message, StringComparison.Ordinal);
    }

    // ---- Email ---------------------------------------------------------------

    [Theory]
    [InlineData("kontakt@example.test")]
    [InlineData("info@alpha-testbetrieb.test")]
    public void A_plain_email_address_is_accepted(string email)
    {
        new CompanyIdentityOptions { ContactEmail = email }.Validate();
    }

    [Theory]
    [InlineData("Testfirma <kontakt@example.test>")]
    [InlineData("kontakt@example.test, info@example.test")]
    [InlineData("kontakt@example.test;info@example.test")]
    [InlineData("kontakt @example.test")]
    [InlineData("kontakt")]
    [InlineData("\"kontakt\"@example.test ")]
    public void A_display_name_a_list_or_a_malformed_address_is_refused(string email)
    {
        var error = Refused(new CompanyIdentityOptions { ContactEmail = email });

        Assert.Contains("'CompanyIdentity:ContactEmail'", error.Message, StringComparison.Ordinal);
    }

    // ---- Address -------------------------------------------------------------

    [Fact]
    public void A_whole_address_is_accepted_and_no_address_is_valid_too()
    {
        new CompanyIdentityOptions { Address = WholeAddress() }.Validate();
        new CompanyIdentityOptions().Validate();
    }

    [Fact]
    public void Half_an_address_is_refused_naming_every_missing_part()
    {
        var error = Refused(new CompanyIdentityOptions
        {
            Address = new PostalAddressOptions { StreetAddress = "Teststraße 1", CountryCode = "DE" },
        });

        Assert.Contains("'CompanyIdentity:Address:PostalCode'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:Address:Locality'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("DEU")]
    [InlineData("XX")]
    [InlineData("D1")]
    public void A_country_code_that_is_not_iso_alpha_2_is_refused(string code)
    {
        var address = WholeAddress();
        var error = Refused(new CompanyIdentityOptions
        {
            Address = new PostalAddressOptions
            {
                StreetAddress = address.StreetAddress, PostalCode = address.PostalCode,
                Locality = address.Locality, CountryCode = code,
            },
        });

        Assert.Contains("'CompanyIdentity:Address:CountryCode'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_postal_code_with_punctuation_is_refused()
    {
        var error = Refused(new CompanyIdentityOptions
        {
            Address = new PostalAddressOptions
            {
                StreetAddress = "Teststraße 1", PostalCode = "000/00", Locality = "Testort", CountryCode = "DE",
            },
        });

        Assert.Contains("'CompanyIdentity:Address:PostalCode'", error.Message, StringComparison.Ordinal);
    }

    // ---- Opening hours -------------------------------------------------------

    private static CompanyIdentityOptions WithHours(params OpeningHoursOptions[] blocks) => new() { OpeningHours = blocks };

    [Fact]
    public void Regular_hours_on_distinct_days_are_accepted()
    {
        WithHours(
            new OpeningHoursOptions { Days = ["Monday", "Tuesday"], Opens = "08:00", Closes = "17:00" },
            new OpeningHoursOptions { Days = ["Friday"], Opens = "08:00", Closes = "12:30" }).Validate();
    }

    [Theory]
    [InlineData("Mon")]
    [InlineData("monday")]
    [InlineData("1")]
    [InlineData("Montag")]
    [InlineData("")]
    public void A_day_that_is_not_an_exact_english_day_name_is_refused(string day)
    {
        var error = Refused(WithHours(new OpeningHoursOptions { Days = ["Monday", day], Opens = "08:00", Closes = "17:00" }));

        Assert.Contains("'CompanyIdentity:OpeningHours:0:Days:1'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8:00", "17:00", "Opens")]
    [InlineData("08:00", "25:00", "Closes")]
    [InlineData("08:00", "5pm", "Closes")]
    [InlineData(null, "17:00", "Opens")]
    public void A_time_that_is_not_24_hour_hh_mm_is_refused(string? opens, string closes, string property)
    {
        var error = Refused(WithHours(new OpeningHoursOptions { Days = ["Monday"], Opens = opens, Closes = closes }));

        Assert.Contains($"'CompanyIdentity:OpeningHours:0:{property}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("17:00", "08:00")]
    [InlineData("08:00", "08:00")]
    public void Closing_must_be_later_than_opening(string opens, string closes)
    {
        var error = Refused(WithHours(new OpeningHoursOptions { Days = ["Monday"], Opens = opens, Closes = closes }));

        Assert.Contains("'CompanyIdentity:OpeningHours:0:Closes'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_with_no_days_is_refused()
    {
        var error = Refused(WithHours(new OpeningHoursOptions { Opens = "08:00", Closes = "17:00" }));

        Assert.Contains("'CompanyIdentity:OpeningHours:0:Days'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_day_twice_in_one_block_is_refused()
    {
        var error = Refused(WithHours(new OpeningHoursOptions { Days = ["Monday", "Monday"], Opens = "08:00", Closes = "17:00" }));

        Assert.Contains("'CompanyIdentity:OpeningHours:0:Days'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Saturday by appointment" is not a span; it belongs in the note, and a split day is not modelled.
    /// </summary>
    [Fact]
    public void The_same_day_in_two_blocks_is_refused()
    {
        var error = Refused(WithHours(
            new OpeningHoursOptions { Days = ["Monday"], Opens = "08:00", Closes = "12:00" },
            new OpeningHoursOptions { Days = ["Monday"], Opens = "13:00", Closes = "17:00" }));

        Assert.Contains("'CompanyIdentity:OpeningHours'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Monday", error.Message, StringComparison.Ordinal);
    }

    // ---- Service area --------------------------------------------------------

    [Fact]
    public void Named_cities_and_regions_are_accepted()
    {
        new CompanyIdentityOptions
        {
            ServiceArea = new ServiceAreaOptions
            {
                Places = [new() { Name = "Testort", Kind = "City" }, new() { Name = "Testregion", Kind = "Region" }],
                Note = "Weitere Orte nach Absprache.",
            },
        }.Validate();
    }

    [Theory]
    [InlineData("Town")]
    [InlineData("city")]
    [InlineData(null)]
    public void A_place_kind_other_than_city_or_region_is_refused(string? kind)
    {
        var error = Refused(new CompanyIdentityOptions
        {
            ServiceArea = new ServiceAreaOptions { Places = [new() { Name = "Testort", Kind = kind }] },
        });

        Assert.Contains("'CompanyIdentity:ServiceArea:Places:0:Kind'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_place_without_a_name_is_refused()
    {
        var error = Refused(new CompanyIdentityOptions
        {
            ServiceArea = new ServiceAreaOptions { Places = [new() { Name = " ", Kind = "City" }] },
        });

        Assert.Contains("'CompanyIdentity:ServiceArea:Places:0:Name'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_place_twice_is_refused_regardless_of_case()
    {
        var error = Refused(new CompanyIdentityOptions
        {
            ServiceArea = new ServiceAreaOptions
            {
                Places = [new() { Name = "Testort", Kind = "City" }, new() { Name = "TESTORT", Kind = "Region" }],
            },
        });

        Assert.Contains("'CompanyIdentity:ServiceArea:Places'", error.Message, StringComparison.Ordinal);
    }

    // ---- Text rules on every field ------------------------------------------

    public static TheoryData<string, CompanyIdentityOptions> ControlCharacterCases()
    {
        const string broken = "Zeile eins\nZeile zwei";
        var address = WholeAddress();

        return new TheoryData<string, CompanyIdentityOptions>
        {
            { "CompanyIdentity:DisplayName", new() { DisplayName = broken } },
            { "CompanyIdentity:OwnerName", new() { OwnerName = broken } },
            { "CompanyIdentity:OpeningHoursNote", new() { OpeningHoursNote = broken } },
            { "CompanyIdentity:ServiceArea:Note", new() { ServiceArea = new() { Note = broken } } },
            { "CompanyIdentity:ServiceArea:Places:0:Name", new() { ServiceArea = new() { Places = [new() { Name = broken, Kind = "City" }] } } },
            { "CompanyIdentity:Address:StreetAddress", new() { Address = new() { StreetAddress = broken, PostalCode = address.PostalCode, Locality = address.Locality, CountryCode = "DE" } } },
            { "CompanyIdentity:Address:Locality", new() { Address = new() { StreetAddress = address.StreetAddress, PostalCode = address.PostalCode, Locality = broken, CountryCode = "DE" } } },
        };
    }

    [Theory]
    [MemberData(nameof(ControlCharacterCases))]
    public void A_control_character_in_any_text_field_is_refused_naming_the_key(string key, CompanyIdentityOptions options)
    {
        var error = Refused(options);

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, CompanyIdentityOptions> OverLongCases()
    {
        var address = WholeAddress();

        return new TheoryData<string, CompanyIdentityOptions>
        {
            { "CompanyIdentity:DisplayName", new() { DisplayName = new string('a', 101) } },
            { "CompanyIdentity:OwnerName", new() { OwnerName = new string('a', 101) } },
            { "CompanyIdentity:OpeningHoursNote", new() { OpeningHoursNote = new string('a', 201) } },
            { "CompanyIdentity:ServiceArea:Note", new() { ServiceArea = new() { Note = new string('a', 301) } } },
            { "CompanyIdentity:ServiceArea:Places:0:Name", new() { ServiceArea = new() { Places = [new() { Name = new string('a', 81), Kind = "City" }] } } },
            { "CompanyIdentity:Address:StreetAddress", new() { Address = new() { StreetAddress = new string('a', 101), PostalCode = address.PostalCode, Locality = address.Locality, CountryCode = "DE" } } },
            { "CompanyIdentity:Address:PostalCode", new() { Address = new() { StreetAddress = address.StreetAddress, PostalCode = new string('1', 11), Locality = address.Locality, CountryCode = "DE" } } },
            { "CompanyIdentity:Address:Locality", new() { Address = new() { StreetAddress = address.StreetAddress, PostalCode = address.PostalCode, Locality = new string('a', 81), CountryCode = "DE" } } },
        };
    }

    [Theory]
    [MemberData(nameof(OverLongCases))]
    public void An_over_long_text_field_is_refused_naming_the_key(string key, CompanyIdentityOptions options)
    {
        var error = Refused(options);

        Assert.Contains($"'{key}'", error.Message, StringComparison.Ordinal);
    }

    // ---- What a marketing site requires -------------------------------------

    [Fact]
    public void An_empty_identity_is_missing_exactly_name_phone_email_and_address()
    {
        Assert.Equal(
            new[]
            {
                "CompanyIdentity:DisplayName",
                "CompanyIdentity:ContactPhone",
                "CompanyIdentity:ContactEmail",
                "CompanyIdentity:Address",
            },
            new CompanyIdentityOptions().MissingForMarketingSite());
    }

    [Fact]
    public void Hours_area_and_owner_are_not_required_for_a_marketing_site()
    {
        var identity = new CompanyIdentityOptions
        {
            DisplayName = "Testfirma",
            ContactPhone = "+49 000 0000000",
            ContactEmail = "kontakt@example.test",
            Address = WholeAddress(),
        };

        Assert.Empty(identity.MissingForMarketingSite());
    }
}
