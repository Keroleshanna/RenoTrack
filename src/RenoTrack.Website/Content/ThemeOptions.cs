using System.Globalization;
using System.Text.RegularExpressions;

namespace RenoTrack.Website.Content;

/// <summary>
/// The company's two brand colours, bound from <c>Site:Theme</c> (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Colours, not CSS.</b> A company-authored stylesheet could not be validated and inline styles would
/// break the marketing CSP (<c>style-src 'self'</c>). Two <c>#RRGGBB</c> values are the whole surface: the
/// application generates the stylesheet from them, so nothing else can reach it.
/// </para>
/// <para>
/// <b>Contrast is checked at startup, not hoped for.</b> The primary colour carries white button text and
/// link text, so it must reach 4.5:1 against white. The accent is used only for decoration, borders and
/// large text on the primary, so it must reach 3:1 against the effective primary. A company whose brand
/// colour fails that is told at startup, naming the key, rather than shipping unreadable buttons.
/// </para>
/// <para>
/// Absent values fall back to neutral product defaults, which are not any company's colours.
/// </para>
/// </remarks>
public sealed partial class ThemeOptions
{
    /// <summary>Neutral product default: a dark slate blue.</summary>
    public const string DefaultPrimaryColor = "#2B3A4A";

    /// <summary>Neutral product default: a light grey-blue.</summary>
    public const string DefaultAccentColor = "#C8D3DE";

    internal const double MinimumPrimaryContrastOnWhite = 4.5;
    internal const double MinimumAccentContrastOnPrimary = 3.0;

    public string? PrimaryColor { get; init; }

    public string? AccentColor { get; init; }

    /// <summary>The primary colour in use: the configured one, or the product default. Upper case.</summary>
    public string EffectivePrimaryColor => Normalise(PrimaryColor) ?? DefaultPrimaryColor;

    /// <summary>The accent colour in use: the configured one, or the product default. Upper case.</summary>
    public string EffectiveAccentColor => Normalise(AccentColor) ?? DefaultAccentColor;

    /// <exception cref="InvalidOperationException">A colour is malformed or fails its contrast minimum.</exception>
    internal void Validate(string path)
    {
        var primaryKey = $"{path}:{nameof(PrimaryColor)}";
        var accentKey = $"{path}:{nameof(AccentColor)}";

        EnsureHex(PrimaryColor, primaryKey);
        EnsureHex(AccentColor, accentKey);

        var primaryOnWhite = ContrastRatio(EffectivePrimaryColor, "#FFFFFF");
        if (primaryOnWhite < MinimumPrimaryContrastOnWhite)
        {
            throw new InvalidOperationException(
                $"Configuration '{primaryKey}' has a contrast of {primaryOnWhite:0.00}:1 against white; at least " +
                $"{MinimumPrimaryContrastOnWhite:0.0}:1 is required, because it carries white button text and link text.");
        }

        var accentOnPrimary = ContrastRatio(EffectiveAccentColor, EffectivePrimaryColor);
        if (accentOnPrimary < MinimumAccentContrastOnPrimary)
        {
            throw new InvalidOperationException(
                $"Configuration '{accentKey}' has a contrast of {accentOnPrimary:0.00}:1 against the primary colour; " +
                $"at least {MinimumAccentContrastOnPrimary:0.0}:1 is required.");
        }
    }

    /// <summary>The WCAG 2 contrast ratio of two <c>#RRGGBB</c> colours.</summary>
    internal static double ContrastRatio(string first, string second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(hex, 1)) + (0.7152 * Channel(hex, 3)) + (0.0722 * Channel(hex, 5));
    }

    private static void EnsureHex(string? value, string key)
    {
        if (value is not null && !HexColorPattern().IsMatch(value))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a colour written as '#RRGGBB', e.g. '#2B3A4A'.");
        }
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorPattern();
}
