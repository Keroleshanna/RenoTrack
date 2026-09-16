namespace RenoTrack.Website.Content;

/// <summary>
/// The rules every piece of configured company text shares: no control characters, and a length
/// that a page can actually lay out.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are shape checks, never truth checks.</b> Whether the address is right is the company's
/// to verify in its own content repository; code only refuses what cannot be rendered as intended.
/// </para>
/// <para>
/// A newline in a company name or a service heading is almost always a paste accident, and it reaches
/// a page title, a heading and — in a later slice — structured data. Refusing it at startup is cheaper
/// than discovering it in a search result.
/// </para>
/// </remarks>
internal static class ContentText
{
    /// <summary>Fails when a supplied value contains a control character or exceeds <paramref name="maxLength"/>.</summary>
    /// <remarks>Absent values are not this method's concern; required-ness is checked by the caller.</remarks>
    internal static void Validate(string? value, string key, int maxLength)
    {
        if (value is null)
        {
            return;
        }

        if (value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' contains a control character such as a line break or a tab. " +
                "Company text is single-line plain text; remove the character.");
        }

        if (value.Trim().Length > maxLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' is {value.Trim().Length} characters long; the limit is {maxLength}.");
        }
    }

    internal static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
