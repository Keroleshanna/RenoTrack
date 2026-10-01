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
        Assert.True(ThemeOptions.ContrastRatio(ThemeOptions.DefaultAccentColor, theme.EffectiveNightColor) >= 4.5);
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

    /// <summary>
    /// The accent fills the primary button and carries its night-coloured label, so 3:1 is no longer enough
    /// (Slice 5v, <b>D107</b>): it must reach text contrast against the dark surface.
    /// </summary>
    [Fact]
    public void An_accent_below_four_and_a_half_to_one_against_the_dark_surface_is_refused()
    {
        var error = Refused(new ThemeOptions { PrimaryColor = "#1F4B7A", AccentColor = "#2F5B8A" });

        Assert.Contains("'Site:Theme:AccentColor'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contrast_is_checked_against_the_effective_dark_surface_when_only_the_accent_is_configured()
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

    /// <summary>
    /// The generated stylesheet carries the two configured colours and the four roles derived from them
    /// (<b>D107</b>) — nothing else, all upper case, so no configured value can add a declaration of its own.
    /// </summary>
    [Fact]
    public void The_stylesheet_carries_the_configured_colours_and_their_derived_roles_in_upper_case()
    {
        var theme = new ThemeOptions { PrimaryColor = "#1f4b7a", AccentColor = "#e8c27a" };

        var stylesheet = new ThemeStylesheet(theme);

        Assert.Equal(
            ":root {\n"
            + "    --brand-primary: #1F4B7A;\n"
            + "    --brand-accent: #E8C27A;\n"
            + $"    --brand-night: {theme.EffectiveNightColor};\n"
            + $"    --brand-navy: {theme.EffectiveNavyColor};\n"
            + $"    --brand-accent-bright: {theme.EffectiveAccentBrightColor};\n"
            + $"    --brand-accent-strong: {theme.EffectiveAccentStrongColor};\n"
            + "}\n",
            stylesheet.Content);
        Assert.Matches("^#[0-9A-F]{6}$", theme.EffectiveNightColor);
        Assert.Matches("^/site/theme\\.css\\?v=[0-9a-f]{12}$", stylesheet.Url);
    }

    // ---- Derived roles (Slice 5v, D107) ---------------------------------------------

    /// <summary>
    /// Each derived role reaches what the visual system asks of it, for the product defaults and for brand pairs
    /// alike. These are the pairings marketing.css actually uses.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("#1F2B37", "#B8894A")]
    [InlineData("#2B3A4A", "#C8D3DE")]
    [InlineData("#14532D", "#E8C27A")]
    public void Every_derived_role_reaches_the_contrast_the_visual_system_needs(string? primary, string? accent)
    {
        var theme = new ThemeOptions { PrimaryColor = primary, AccentColor = accent };

        new SiteOptions { Theme = theme }.Validate(new CompanyIdentityOptions());

        Assert.True(ThemeOptions.ContrastRatio("#FFFFFF", theme.EffectiveNightColor) >= 12.0, "white text on the night band");
        Assert.True(ThemeOptions.ContrastRatio("#FFFFFF", theme.EffectiveNavyColor) >= 4.5, "white text on the second dark band");
        Assert.True(ThemeOptions.ContrastRatio("#B9C2CC", theme.EffectiveNightColor) >= 4.5, "muted text on the night band");
        Assert.True(ThemeOptions.ContrastRatio(theme.EffectiveAccentBrightColor, theme.EffectiveNightColor) >= 7.0, "accent text on the night band");
        Assert.True(ThemeOptions.ContrastRatio(theme.EffectiveAccentStrongColor, ThemeOptions.StoneColor) >= 4.5, "accent text on the warm surface");
        // The darker light surface is the one that decides: accent text measured 4.11:1 on the breadcrumb bar
        // when only stone was checked (Slice 5v checkpoint).
        Assert.True(ThemeOptions.ContrastRatio(theme.EffectiveAccentStrongColor, ThemeOptions.SandColor) >= 4.5, "accent text on the darker warm surface");
        Assert.True(ThemeOptions.ContrastRatio("#1A2129", ThemeOptions.SandColor) >= 4.5, "body text on the darker warm surface");
        Assert.True(ThemeOptions.ContrastRatio(theme.EffectiveAccentColor, theme.EffectiveNightColor) >= 4.5, "the primary button's label");
        Assert.True(ThemeOptions.ContrastRatio("#1A2129", ThemeOptions.StoneColor) >= 4.5, "body text on the warm surface");
    }

    /// <summary>The two dark surfaces have to differ, or the alternating rhythm reads as one long dark band.</summary>
    [Fact]
    public void The_night_surface_is_darker_than_the_second_dark_band()
    {
        var theme = new ThemeOptions { PrimaryColor = "#1F2B37" };

        Assert.NotEqual(theme.EffectiveNightColor, theme.EffectiveNavyColor);
        Assert.True(ThemeOptions.ContrastRatio("#FFFFFF", theme.EffectiveNightColor)
            > ThemeOptions.ContrastRatio("#FFFFFF", theme.EffectiveNavyColor));
    }

    /// <summary>
    /// Derivation is deterministic: the same two colours always generate the same stylesheet, which is what the
    /// content hash in its URL assumes.
    /// </summary>
    [Fact]
    public void The_same_colours_always_derive_the_same_roles()
    {
        var first = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F2B37", AccentColor = "#B8894A" });
        var second = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F2B37", AccentColor = "#B8894A" });

        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.Url, second.Url);
    }

    /// <summary>An accent already readable on the dark band is used unchanged, not lightened for its own sake.</summary>
    [Fact]
    public void An_accent_that_already_clears_the_bright_target_is_left_alone()
    {
        var theme = new ThemeOptions { PrimaryColor = "#1F2B37", AccentColor = "#E8DCC8" };

        Assert.Equal(theme.EffectiveAccentColor, theme.EffectiveAccentBrightColor);
    }

    [Fact]
    public void A_colour_change_changes_the_stylesheet_url()
    {
        var first = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F4B7A" });
        var second = new ThemeStylesheet(new ThemeOptions { PrimaryColor = "#1F4B7B" });

        Assert.NotEqual(first.Url, second.Url);
    }
}
