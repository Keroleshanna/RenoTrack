using System.Globalization;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Common.Documents;

/// <summary>
/// The one tested place a document's figures are formatted (Phase 14 Slice 2, <b>D111</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>An explicit <c>de-DE</c> culture, never the ambient one.</b> A server in another locale would
/// otherwise print <c>1,234.56</c> on a German invoice. The Website's <c>CustomerFormatting</c> makes
/// the same choice for the same reason; the Website references no backend project, so the two are
/// separate by the layering rule, not by oversight (CLAUDE.md §1).
/// </para>
/// <para>
/// <b>It formats; it never rounds or recomputes.</b> Every amount reaching here is already exact to
/// two places (BR-11, <c>Money</c>), so <c>N2</c> only adds separators.
/// </para>
/// </remarks>
public static class DocumentFormatting
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>A money amount as printed, e.g. <c>1.234,56 €</c>.</summary>
    public static string Money(decimal amount) => amount.ToString("N2", German) + " €";

    /// <summary>A VAT rate as printed, e.g. <c>19 %</c>, <c>0 %</c>.</summary>
    public static string VatRate(VatRate rate) => rate.ToPercentage().ToString("0.##", German) + " %";
}
