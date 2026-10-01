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
/// link text, so it must reach 4.5:1 against white. A company whose brand colour fails that is told at
/// startup, naming the key, rather than shipping unreadable buttons.
/// </para>
/// <para>
/// <b>Two inputs, several surface roles (Slice 5v, <b>D107</b>).</b> The visual system needs more surfaces
/// than two colours name: a near-black band for the header, hero and footer, a second dark band so two dark
/// sections never touch, accent text readable on a dark surface, and accent text readable on a light one.
/// Those are <em>derived</em> here, in C#, rather than with CSS <c>color-mix()</c>, for one reason: a derived
/// colour that carries text must be contrast-checked at startup exactly like a configured one, and CSS cannot
/// report a failure. The company still supplies exactly two values.
/// </para>
/// <para>
/// <b>The accent minimum rose from 3:1 to 4.5:1 (D107).</b> It used to be decoration, borders and large text
/// only; it now fills the primary button, whose label is <see cref="EffectiveNightColor"/>. Contrast is
/// symmetric, so that single check covers both the button's fill and its label.
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

    /// <summary>The accent fills buttons labelled in the night colour, so it carries text (<b>D107</b>).</summary>
    internal const double MinimumAccentContrastOnNight = 4.5;

    /// <summary>What accent text on a dark surface must reach: body-text contrast with headroom.</summary>
    internal const double TargetAccentBrightContrastOnNight = 7.0;

    /// <summary>What accent text on a warm light surface must reach.</summary>
    internal const double TargetAccentStrongContrastOnStone = 4.5;

    /// <summary>
    /// The warm off-white the light sections use. A product neutral, deliberately not a brand colour: it has to
    /// sit under every company's palette.
    /// </summary>
    internal const string StoneColor = "#F5F1EA";

    /// <summary>
    /// The second, darker light surface (the alternate band and the breadcrumb bar), and what
    /// <see cref="EffectiveAccentStrongColor"/> is measured against.
    /// </summary>
    /// <remarks>
    /// <b>The darker of the two light surfaces is the one that decides.</b> Deriving accent text against stone
    /// alone produced 4.11:1 on the breadcrumb bar — measured at the Slice 5v prototype checkpoint, not predicted.
    /// A colour that clears 4.5:1 here clears it on stone as well.
    /// </remarks>
    internal const string SandColor = "#EAE2D5";

    /// <summary>How far the night surface is mixed towards black from the primary colour.</summary>
    private const double NightMixTowardsBlack = 0.40;

    public string? PrimaryColor { get; init; }

    public string? AccentColor { get; init; }

    /// <summary>The primary colour in use: the configured one, or the product default. Upper case.</summary>
    public string EffectivePrimaryColor => Normalise(PrimaryColor) ?? DefaultPrimaryColor;

    /// <summary>The accent colour in use: the configured one, or the product default. Upper case.</summary>
    public string EffectiveAccentColor => Normalise(AccentColor) ?? DefaultAccentColor;

    /// <summary>
    /// The near-black band behind the header, the hero and the footer: the primary mixed towards black
    /// (<b>D107</b>). Derived rather than configured, so a company still supplies two colours.
    /// </summary>
    public string EffectiveNightColor => MixTowardsBlack(EffectivePrimaryColor, NightMixTowardsBlack);

    /// <summary>
    /// The second dark band, which is the primary itself. Two dark sections never touch (the page alternates
    /// between this and <see cref="EffectiveNightColor"/>), and the primary already reaches 4.5:1 against white,
    /// so it is dark enough to carry white text.
    /// </summary>
    public string EffectiveNavyColor => EffectivePrimaryColor;

    /// <summary>
    /// Accent <em>text</em> on a dark surface: the accent lightened until it reaches
    /// <see cref="TargetAccentBrightContrastOnNight"/> against <see cref="EffectiveNightColor"/>. An accent that
    /// already reaches it is used unchanged.
    /// </summary>
    public string EffectiveAccentBrightColor =>
        Reach(EffectiveAccentColor, EffectiveNightColor, TargetAccentBrightContrastOnNight, towards: "#FFFFFF");

    /// <summary>
    /// Accent <em>text</em> on a light surface: the accent darkened until it reaches
    /// <see cref="TargetAccentStrongContrastOnStone"/> against <see cref="SandColor"/>, the darker of the two
    /// light surfaces — so it clears the target on both.
    /// </summary>
    /// <remarks>
    /// This role exists because a mid-tone brand accent is unreadable as small text on a warm off-white — the
    /// deployment bronze measures 2.78:1 there — while being exactly right as a button fill on a dark band. One
    /// value cannot do both, and the page needs both.
    /// </remarks>
    public string EffectiveAccentStrongColor =>
        Reach(EffectiveAccentColor, SandColor, TargetAccentStrongContrastOnStone, towards: "#000000");

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

        // Against the derived night surface, not the primary: the accent fills the primary button, which sits on
        // the dark bands and is labelled in the night colour (D107).
        var accentOnNight = ContrastRatio(EffectiveAccentColor, EffectiveNightColor);
        if (accentOnNight < MinimumAccentContrastOnNight)
        {
            throw new InvalidOperationException(
                $"Configuration '{accentKey}' has a contrast of {accentOnNight:0.00}:1 against the dark surface " +
                $"derived from the primary colour; at least {MinimumAccentContrastOnNight:0.0}:1 is required, " +
                "because it fills the primary button and carries that button's label.");
        }
    }

    /// <summary>The WCAG 2 contrast ratio of two <c>#RRGGBB</c> colours.</summary>
    internal static double ContrastRatio(string first, string second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// <paramref name="colour"/> moved towards <paramref name="towards"/> in fixed 2 % steps until it reaches
    /// <paramref name="target"/> against <paramref name="background"/>, or the endpoint if it never does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed steps, so the result is deterministic</b> — the same two configured colours always generate the
    /// same stylesheet, which is what the content hash on <c>/site/theme.css</c> assumes.
    /// </para>
    /// <para>
    /// <b>The endpoint can always satisfy the targets in use</b>: white on the night surface exceeds 15:1 and
    /// black on the stone surface exceeds 18:1, both far above what is asked for here. Returning the endpoint
    /// rather than throwing is therefore not a silent failure — it is the last step of the same walk.
    /// </para>
    /// </remarks>
    private static string Reach(string colour, string background, double target, string towards)
    {
        for (var step = 0; step <= 50; step++)
        {
            var candidate = Mix(colour, towards, step * 0.02);
            if (ContrastRatio(candidate, background) >= target)
            {
                return candidate;
            }
        }

        return Normalise(towards)!;
    }

    private static string MixTowardsBlack(string colour, double amount) => Mix(colour, "#000000", amount);

    /// <summary>Channel-wise linear blend of two <c>#RRGGBB</c> colours, rounded to the nearest byte.</summary>
    private static string Mix(string from, string to, double amount)
    {
        static int Channel(string hex, int offset) =>
            int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var mixed = new[] { 1, 3, 5 }.Select(offset =>
        {
            var start = Channel(from, offset);
            var end = Channel(to, offset);
            return (int)Math.Round(start + ((end - start) * amount), MidpointRounding.AwayFromZero);
        });

        return "#" + string.Concat(mixed.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
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
