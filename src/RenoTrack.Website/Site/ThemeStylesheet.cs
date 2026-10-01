using System.Security.Cryptography;
using System.Text;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>
/// The generated brand stylesheet, <c>/site/theme.css</c> (<b>D103</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Custom properties only, built from validated colours.</b> Only <c>#RRGGBB</c> values reach the string
/// (<see cref="ThemeOptions"/>), so no configured value can close the rule or add a declaration of its own.
/// A company-authored CSS file was rejected because it could not be validated, and inline styles because
/// the marketing CSP forbids them.
/// </para>
/// <para>
/// Built once at startup and always served: with no <c>Site:Theme</c>, or with the marketing site disabled,
/// it carries the neutral product defaults.
/// </para>
/// </remarks>
public sealed class ThemeStylesheet
{
    public const string Path = "/site/theme.css";

    public ThemeStylesheet(ThemeOptions theme)
    {
        // Two configured colours, several validated roles (D107): marketing.css consumes the roles and never
        // re-derives one, so no surface can end up with a contrast nobody checked.
        Content =
            ":root {\n" +
            $"    --brand-primary: {theme.EffectivePrimaryColor};\n" +
            $"    --brand-accent: {theme.EffectiveAccentColor};\n" +
            $"    --brand-night: {theme.EffectiveNightColor};\n" +
            $"    --brand-navy: {theme.EffectiveNavyColor};\n" +
            $"    --brand-accent-bright: {theme.EffectiveAccentBrightColor};\n" +
            $"    --brand-accent-strong: {theme.EffectiveAccentStrongColor};\n" +
            "}\n";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Content));
        Url = $"{Path}?v={Convert.ToHexStringLower(hash)[..12]}";
    }

    public string Content { get; }

    /// <summary>The stylesheet's URL with a content hash, so a colour change is never served stale.</summary>
    public string Url { get; }

    /// <summary>Maps <see cref="Path"/>. Cacheable for an hour; the hashed URL makes a change visible at once.</summary>
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints, ThemeStylesheet stylesheet) =>
        endpoints.MapGet(Path, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(stylesheet.Content, "text/css; charset=utf-8");
        });
}
