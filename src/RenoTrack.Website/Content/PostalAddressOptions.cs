using System.Globalization;
using System.Text.RegularExpressions;

namespace RenoTrack.Website.Content;

/// <summary>The company's postal address: all four parts, or none.</summary>
/// <remarks>
/// <b>Half an address is refused rather than rendered.</b> A street without a town, or a town
/// without a postal code, cannot be shown or published consistently, and a later slice publishes the
/// same address as structured data — where a partial one is worse than none.
/// </remarks>
public sealed partial class PostalAddressOptions
{
    internal const int MaxStreetAddressLength = 100;
    internal const int MaxPostalCodeLength = 10;
    internal const int MaxLocalityLength = 80;

    public string? StreetAddress { get; init; }

    public string? PostalCode { get; init; }

    public string? Locality { get; init; }

    /// <summary>ISO 3166-1 alpha-2, upper case, e.g. <c>DE</c>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>Whether any part is supplied. Validation then requires all of them.</summary>
    public bool IsSupplied =>
        !ContentText.IsBlank(StreetAddress) || !ContentText.IsBlank(PostalCode)
        || !ContentText.IsBlank(Locality) || !ContentText.IsBlank(CountryCode);

    internal void Validate(string path)
    {
        if (!IsSupplied)
        {
            return;
        }

        var missing = new List<string>();
        if (ContentText.IsBlank(StreetAddress)) missing.Add($"{path}:{nameof(StreetAddress)}");
        if (ContentText.IsBlank(PostalCode)) missing.Add($"{path}:{nameof(PostalCode)}");
        if (ContentText.IsBlank(Locality)) missing.Add($"{path}:{nameof(Locality)}");
        if (ContentText.IsBlank(CountryCode)) missing.Add($"{path}:{nameof(CountryCode)}");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}' is only partly supplied; missing {string.Join(", ", missing.Select(key => $"'{key}'"))}. " +
                "An address is supplied whole or not at all.");
        }

        ContentText.Validate(StreetAddress, $"{path}:{nameof(StreetAddress)}", MaxStreetAddressLength);
        ContentText.Validate(PostalCode, $"{path}:{nameof(PostalCode)}", MaxPostalCodeLength);
        ContentText.Validate(Locality, $"{path}:{nameof(Locality)}", MaxLocalityLength);

        // Country-neutral on purpose: the product is not German-only in principle, so a postal code
        // is checked for plausible characters rather than for any one country's format.
        if (!PostalCodePattern().IsMatch(PostalCode!.Trim()))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(PostalCode)}' may contain only letters, digits, spaces and hyphens.");
        }

        var country = CountryCode!.Trim();
        if (!CountryCodePattern().IsMatch(country) || !IsKnownRegion(country))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(CountryCode)}' must be an ISO 3166-1 alpha-2 country code " +
                "in upper case, e.g. 'DE'.");
        }
    }

    private static bool IsKnownRegion(string code)
    {
        try
        {
            return string.Equals(new RegionInfo(code).TwoLetterISORegionName, code, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 -]*$")]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryCodePattern();
}
