using RenoTrack.Website.Content;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>German presentation of company facts on the marketing site (<b>D103</b>).</summary>
public sealed class SiteFormattingTests
{
    private const string Dash = "–";

    private static OpeningHoursOptions Block(string opens, string closes, params string[] days) =>
        new() { Days = days, Opens = opens, Closes = closes };

    [Fact]
    public void Five_consecutive_weekdays_become_a_range()
    {
        var line = Assert.Single(SiteFormatting.OpeningHours([Block("08:00", "18:00", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday")]));

        Assert.Equal($"Mo{Dash}Fr", line.DaysShort);
        Assert.Equal("Montag bis Freitag", line.DaysLong);
        Assert.Equal($"08:00{Dash}18:00 Uhr", line.Times);
    }

    [Fact]
    public void Non_consecutive_days_are_listed()
    {
        var line = Assert.Single(SiteFormatting.OpeningHours([Block("09:00", "12:00", "Friday", "Monday", "Wednesday")]));

        Assert.Equal("Mo, Mi, Fr", line.DaysShort);
        Assert.Equal("Montag, Mittwoch und Freitag", line.DaysLong);
    }

    [Theory]
    [InlineData(new[] { "Saturday" }, "Sa", "Samstag")]
    [InlineData(new[] { "Saturday", "Sunday" }, "Sa, So", "Samstag und Sonntag")]
    [InlineData(new[] { "Friday", "Saturday", "Sunday" }, "Fr–So", "Freitag bis Sonntag")]
    [InlineData(new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }, "Mo–So", "Montag bis Sonntag")]
    public void Days_are_ordered_monday_first_and_ranged_from_three(string[] days, string expectedShort, string expectedLong)
    {
        var line = Assert.Single(SiteFormatting.OpeningHours([Block("08:00", "12:00", days)]));

        Assert.Equal(expectedShort, line.DaysShort);
        Assert.Equal(expectedLong, line.DaysLong);
    }

    [Fact]
    public void Blocks_are_ordered_by_their_first_day()
    {
        var lines = SiteFormatting.OpeningHours(
        [
            Block("09:00", "12:00", "Saturday"),
            Block("08:00", "18:00", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday"),
        ]);

        Assert.Equal(new[] { $"Mo{Dash}Fr", "Sa" }, lines.Select(line => line.DaysShort));
    }

    [Fact]
    public void No_blocks_means_no_lines()
    {
        Assert.Empty(SiteFormatting.OpeningHours([]));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A und B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B und C")]
    [InlineData(new[] { "A", "B", "C", "D" }, "A, B, C und D")]
    public void Lists_are_joined_the_german_way(string[] items, string expected)
    {
        Assert.Equal(expected, SiteFormatting.JoinGerman(items));
    }

    [Fact]
    public void Service_area_places_keep_their_configured_order()
    {
        var area = new ServiceAreaOptions
        {
            Places = [new() { Name = "Testort", Kind = "City" }, new() { Name = " Testregion ", Kind = "Region" }],
        };

        Assert.Equal("Testort und Testregion", SiteFormatting.ServiceAreaPlaces(area));
    }

    [Theory]
    [InlineData("+49 000 1234567", "tel:+490001234567")]
    [InlineData("+490001234567", "tel:+490001234567")]
    [InlineData("+43 1 23456789", "tel:+43123456789")]
    public void A_tel_link_is_the_international_number_without_spaces(string phone, string expected)
    {
        Assert.Equal(expected, SiteFormatting.TelHref(phone));
    }

    [Fact]
    public void A_mailto_link_carries_the_plain_address()
    {
        Assert.Equal("mailto:kontakt@example.test", SiteFormatting.MailtoHref("kontakt@example.test"));
    }

    [Fact]
    public void The_copyright_year_is_taken_in_utc()
    {
        Assert.Equal("2027", SiteFormatting.CopyrightYear(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(-2))));
    }
}
