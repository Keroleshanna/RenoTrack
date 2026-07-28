namespace RenoTrack.Domain.Enums;

/// <summary>
/// The closed set of VAT rates line items can carry. BR-6 requires VAT to be set per line
/// item, not per document, because a single Angebot legitimately mixes multiple rates —
/// observed directly in the company's real reference document, not a hypothetical.
///
/// Modeled as an enum rather than a configurable/editable rate table per Architecture.md §11:
/// "enum is simpler and safer for v1 given the company's real documents only use a known
/// small set of rates." This is a deliberate, documented trade-off (not an oversight): German
/// VAT rates can and have changed by legislation (e.g. the temporary 2020 reduction), and an
/// enum means a future rate change requires a code change and redeploy rather than an Admin
/// editing a lookup table. If that flexibility is ever needed, this becomes a
/// <c>VatRates</c> table + migration — every consumer already goes through this single type,
/// so the blast radius of that future change is contained.
/// </summary>
public enum VatRate
{
    /// <summary>0% — e.g. certain exempt services. ERD.md, BR-6.</summary>
    Zero = 0,

    /// <summary>7% — reduced German VAT rate. ERD.md, BR-6.</summary>
    Reduced = 7,

    /// <summary>16% — seen in the company's real reference Angebot. ERD.md, BR-6.</summary>
    Sixteen = 16,

    /// <summary>19% — standard German VAT rate. ERD.md, BR-6.</summary>
    Standard = 19
}

/// <summary>
/// Converts a <see cref="VatRate"/> to the decimal percentage used in totals math
/// (Architecture.md §6.1), keeping the "enum value equals its percentage" assumption
/// in exactly one place instead of relying on implicit int casts scattered through the code.
/// </summary>
public static class VatRateExtensions
{
    public static decimal ToPercentage(this VatRate rate) => (int)rate;
}
