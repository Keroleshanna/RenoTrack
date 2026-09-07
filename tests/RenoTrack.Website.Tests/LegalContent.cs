using System.Globalization;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Builds <c>Legal:*</c> configuration for a test, as the flat key/value pairs
/// <c>IWebHostBuilder.UseSetting</c> takes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every value here is obviously fake and says so.</b> These are test fixtures, not draft copy:
/// nothing in this repository may carry a company name, an address, a phone number or a sentence of
/// real legal text (Phase 11 Q7, <b>D100</b>), and a plausible-looking placeholder is exactly the
/// thing that eventually gets shipped by accident.
/// </para>
/// <para>
/// Configuration is supplied per test rather than through <c>appsettings.json</c>, so a test that
/// needs an unconfigured document and one that needs a written one can run in the same suite.
/// </para>
/// </remarks>
internal static class LegalContent
{
    /// <summary>German text with the characters that break under the default HTML encoder.</summary>
    internal const string GermanText = "Grundstücksgröße 20 m² – Straße & Gebühren für 100,00 €.";

    /// <summary>
    /// <see cref="GermanText"/> as it must appear in the served HTML.
    /// </summary>
    /// <remarks>
    /// <b>The ampersand is encoded and the German characters are not</b>, and that pairing is the
    /// point of asserting against this rather than against the raw string. <c>&amp;</c> is
    /// HTML-significant so Razor escapes it regardless of encoder range — which is what keeps
    /// operator-supplied text inert — while <c>ü</c>, <c>²</c> and <c>€</c> pass through because
    /// <c>Program.cs</c> widens <c>WebEncoderOptions</c> to <c>UnicodeRanges.All</c>. A test written
    /// against the raw string fails, and it was: the fix was the expectation, not the code
    /// (<c>CLAUDE.md</c> §14).
    /// </remarks>
    internal const string GermanTextRendered =
        "Grundstücksgröße 20 m² – Straße &amp; Gebühren für 100,00 €.";

    internal const string Heading = "Testabschnitt (kein echter Rechtstext)";

    /// <summary>One usable document, enough to make a page render and be linked.</summary>
    internal static Dictionary<string, string?> Document(string document, string? text = null) => new()
    {
        [Key(document, 0, 0, "Text")] = text ?? GermanText,
        [$"Legal:{document}:Sections:0:Heading"] = Heading,
    };

    /// <summary>A document whose single paragraph is a link, for the scheme and rendering tests.</summary>
    internal static Dictionary<string, string?> DocumentWithLink(string document, string url, string label) => new()
    {
        [Key(document, 0, 0, "Text")] = GermanText,
        [Key(document, 0, 0, "LinkText")] = label,
        [Key(document, 0, 0, "LinkUrl")] = url,
        [$"Legal:{document}:Sections:0:Heading"] = Heading,
    };

    internal static string Key(string document, int section, int paragraph, string field) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Legal:{document}:Sections:{section}:Paragraphs:{paragraph}:{field}");

    internal const string Impressum = "Impressum";
    internal const string Datenschutz = "Datenschutz";
}
