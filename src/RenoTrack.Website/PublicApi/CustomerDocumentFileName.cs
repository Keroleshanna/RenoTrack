namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The name a downloaded invoice PDF is saved under, re-derived on this side from what the API
/// declared.
/// </summary>
/// <remarks>
/// <para>
/// <b>The API's <c>Content-Disposition</c> name is not passed on verbatim (D114).</b> It is written
/// into this Website's own response header and onto the customer's disk. The API builds it from an
/// allowlist today (<c>InvoiceDocumentFileName</c>: ASCII letters, digits and <c>-</c>), but this
/// Website references no backend project and must not rely on another process's discipline for a
/// value it emits. The same allowlist is applied again here, so a quote, a path separator, a control
/// character or a non-ASCII character can never reach the header.
/// </para>
/// <para>
/// For the names the API actually sends (<c>Rechnung-RE-2026-00017.pdf</c>) the result is
/// identical, so the customer's download and the email attachment share one name.
/// </para>
/// </remarks>
public static class CustomerDocumentFileName
{
    /// <summary>Used when the API declared no usable name.</summary>
    public const string Fallback = "Rechnung.pdf";

    /// <summary>Generous for <c>Rechnung-{number}</c>, and short enough that no file system objects.</summary>
    public const int MaxStemLength = 100;

    private const string Extension = ".pdf";

    public static string From(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return Fallback;
        }

        // ContentDispositionHeaderValue.FileName keeps the quotes of a quoted-string.
        var name = declared.Trim().Trim('"').Trim();

        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^Extension.Length];
        }

        var stem = new string(name
            .Select(character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray())
            .Trim('-');

        if (stem.Length == 0)
        {
            return Fallback;
        }

        if (stem.Length > MaxStemLength)
        {
            stem = stem[..MaxStemLength].TrimEnd('-');
        }

        return stem + Extension;
    }
}
