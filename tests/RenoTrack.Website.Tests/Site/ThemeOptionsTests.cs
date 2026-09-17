using RenoTrack.Website.Content;
using RenoTrack.Website.Site;

namespace RenoTrack.Website.Tests.Site;

/// <summary>Brand colours: shape, contrast, and the generated stylesheet (<b>D103</b>).</summary>
public sealed class ThemeOptionsTests
{
    private static InvalidOperationException Refused(ThemeOptions theme) =>
        Assert.Throws<InvalidOperationException>(() => new SiteOptions { Theme = theme }.Validate(new CompanyIdentityOptions()));

    [Fact]
    public void No_theme_uses_the_product_defaults_which_meet_both_contrast_minimums()
    {
        var theme = new ThemeOptions();

        new SiteOptions { Theme = theme }.Validate(new CompanyIdentityOptions());

        Assert.Equal(ThemeOptions.DefaultPrimaryColor, theme.EffectivePrimaryColor);
        Assert.Equal(ThemeOptions.DefaultAccentColor, theme.EffectiveAccentColor);
        Assert.True(ThemeOptions.ContrastRatio(ThemeOptions.DefaultPrimaryColor, "#FFFFFF") >= 4.5);
        Assert.True(ThemeOptions.ContrastRatio(ThemeOptions.DefaultAccentColor, ThemeOptions.DefaultPrimaryColor) >= 3.0);
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("red")]
    [InlineData("rgb(0,0,0)")]
    [InlineData("#1F4B7A; background:url(https://example.test)")]
    [InlineData("url(https://example.test/x.png)")]
    [InlineData("#1F4B7AFF")]
    [InlineData("1F4B7A")]
    [InlineData("")]
    public void Anything_but_a_six_digit_hex_colour_is_refused_naming_the_key(string value)
    {
        Assert.Contains("'Site:Theme:PrimaryColor'", Refused(new ThemeOptions { PrimaryColor = value }).Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Theme:AccentColor'", Refused(new ThemeOptions { AccentColor = value }).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#777777")] // 4.48:1 against white
    [InlineData("#FFD700")]
    [InlineData("#FFFFFF")]
    public void A_primary_colour_below_four_and_a_half_to_one_on_white_is_refused(string primary)
    {
        var error = Refused(new ThemeOptions { PrimaryColor = primary });

        Assert.Contains("'Site:Theme:PrimaryColor'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_accent_below_three_to_one_against_the_primary_is_refused()
    {
        var error = Refused(new ThemeOptions { PrimaryColor = "#1F4B7A", AccentColor = "#2F5B8A" });

        Assert.Contains("'Site:Theme:AccentColor'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contrast_is_checked_against_the_effective_primary_when_only_the_accent_is_configured()
    {
        var error = Refused(new ThemeOptions { AccentColor = ThemeOptions.DefaultPrimaryColor });

        Assert.Contains("'Site:Theme:AccentColor'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#767676", "#FFFFFF", 4.54)]
    public void The_contrast_ratio_follows_wcag(string first, string second, double expected)
    {
        Assert.Equal(expected, ThemeOptions.ContrastRatio(first, second), precision: 2);
    }

    [Fact]
    public void The_stylesheet_carries_only_the_two_custom_properties_in_upper_case()
    {
        var stylesheet = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1f4b7a", AccentColor = "#e8c27a" });

        Assert.Equal(":root {\n    --brand-primary: #1F4B7A;\n    --brand-accent: #E8C27A;\n}\n", stylesheet.Content);
        Assert.Matches("^/site/theme\\.css\\?v=[0-9a-f]{12}$", stylesheet.Url);
    }

    [Fact]
    public void A_colour_change_changes_the_stylesheet_url()
    {
        var first = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F4B7A" });
        var second = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F4B7B" });

        Assert.NotEqual(first.Url, second.Url);
    }
}
