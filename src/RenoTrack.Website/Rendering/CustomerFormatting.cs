using System.Globalization;

namespace RenoTrack.Website.Rendering;

/// <summary>
/// How figures are written on a customer's Angebot page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every method formats against an explicit <c>de-DE</c> culture, never the ambient one.</b> A
/// server running under any other locale would otherwise render <c>1,234.56</c> to a German
/// customer reading a German document — a defect that is invisible on a developer's machine and
/// silent in production. This is a formatting decision only: no value is rounded, derived or
/// recalculated here. Money arrives already rounded per BR-11 and every total is the server's
/// (D78).
/// </para>
/// <para>
/// A separate class rather than inline Razor expressions, because formatting is the least
/// test-visible part of a rendered page and the most likely to be quietly wrong.
/// </para>
/// </remarks>
public static class CustomerFormatting
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// A money amount, always to two decimals: <c>1.234,56 €</c>.
    /// </summary>
    /// <remarks>
    /// Two decimals always, including <c>0,00 €</c> — a price written as "5 €" on a quote reads as
    /// an approximation, and `Money` is exact to two places by construction (BR-11).
    /// </remarks>
    public static string Money(decimal amount) => amount.ToString("N2", German) + " €";

    /// <summary>
    /// A quantity, to at most two decimals with trailing zeros trimmed: <c>10</c>, <c>2,5</c>,
    /// <c>0,75</c>.
    /// </summary>
    /// <remarks>
    /// Unlike money, a quantity is a count or a measurement and reads naturally without forced
    /// decimals — "10 m²" rather than "10,00 m²". Two places is the documented display precision
    /// (Phase 11 Q3); it is a rendering choice and never changes the value used in any total.
    /// </remarks>
    public static string Quantity(decimal quantity) =>
        Math.Round(quantity, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", German);

    /// <summary>
    /// A VAT rate as a whole-number percentage: <c>19</c>, for the label <c>zzgl. 19% MwSt</c>.
    /// </summary>
    /// <remarks>
    /// The API sends the percentage itself as a <c>decimal</c> (0/7/16/19). Trailing zeros are
    /// trimmed so a whole rate reads as "19%" rather than "19,00%", while a future fractional rate
    /// would still render rather than being silently truncated.
    /// </remarks>
    public static string VatRate(decimal rate) => rate.ToString("0.##", German);

    /// <summary>
    /// A unit code as a customer should read it.
    /// </summary>
    /// <remarks>
    /// <b>Only <c>m2</c> is rewritten</b>, to <c>m²</c> — it is the one standard code whose storage
    /// form is an ASCII compromise rather than how it is written. Every other code (<c>Stk</c>,
    /// <c>lfm</c>, <c>pauschal</c>, <c>m</c>) is already correct, and <c>ItemUnit</c> is an open
    /// value object, so anything unrecognised is a legitimate custom unit an Inspector typed and
    /// must pass through untouched rather than being mapped, normalised or dropped.
    /// </remarks>
    public static string Unit(string unitCode) =>
        string.Equals(unitCode, "m2", StringComparison.Ordinal) ? "m²" : unitCode;

    /// <summary>
    /// The position number for one line, in Wireframe A3's form: section 1's third line is
    /// <c>1.003</c>.
    /// </summary>
    /// <remarks>
    /// Derived from position, never stored: neither the API nor the Domain carries a position
    /// number, and inventing a stored one would be a second source of truth for something the order
    /// already determines. It is meaningful only because that order is deterministic — see the item
    /// ordering added to <c>PublicAngebotMappingExtensions</c> in the same slice.
    /// </remarks>
    public static string PositionNumber(int sectionIndex, int itemIndex) =>
        FormattableString.Invariant($"{sectionIndex + 1}.{itemIndex + 1:000}");

    /// <summary>The section heading's number, e.g. <c>Pos. 2</c>.</summary>
    public static string SectionNumber(int sectionIndex) =>
        FormattableString.Invariant($"{sectionIndex + 1}");
}
