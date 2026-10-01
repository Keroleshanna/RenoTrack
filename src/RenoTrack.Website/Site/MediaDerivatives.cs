namespace RenoTrack.Website.Site;

/// <summary>The two encodings a published derivative may use (<b>D106</b>).</summary>
public enum MediaFormat
{
    Jpeg,
    WebP,
}

/// <summary>
/// One published derivative of a media item: its file-name suffix, encoding, exact pixel size and byte budget
/// (<b>D106</b>).
/// </summary>
public sealed record MediaDerivative(string Suffix, MediaFormat Format, int Width, int Height, long MaxBytes)
{
    public string Extension => Format == MediaFormat.WebP ? "webp" : "jpg";

    public string ContentType => Format == MediaFormat.WebP ? "image/webp" : "image/jpeg";

    /// <summary>
    /// e.g. <c>badezimmer-01-960.webp</c>. The id is validated as lowercase ASCII with single hyphens before it
    /// gets here, so no path separator or dot sequence can reach a file name.
    /// </summary>
    public string FileNameFor(string id) => $"{id}-{Suffix}.{Extension}";
}

/// <summary>
/// The derivative set every media item is published as — shared, byte for byte, by the Website's startup
/// verification and the offline preparation tool (<c>tools/RenoTrack.MediaPrep</c> links this file), so the
/// two can never disagree about what a valid derivative is (<b>D106</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One content aspect ratio, 3:2, produced by explicit offline crops (S5-5).</b> Pages never crop with CSS, so
/// what the owner approved is exactly what a visitor sees. The social image is its own 1200 × 630 crop.
/// </para>
/// <para>
/// <b>The byte budgets are provisional (S5-11)</b> until they are validated against the real owner-approved
/// derivatives: measured sizes and visual inspection, recorded as aggregates in <c>PHASE13_PROGRESS.md</c> and
/// frozen in D106 and <c>MEDIA_PREPARATION.md</c>. A file over budget is never fixed by degrading that one photo;
/// the global encoding settings or the budget change instead, with evidence. 1 KB here is 1,024 bytes.
/// </para>
/// </remarks>
public static class MediaDerivatives
{
    private const long Kibibyte = 1024;

    /// <summary>Provisional (S5-11): the 1600-wide derivative, per encoding.</summary>
    public const long Large = 350 * Kibibyte;

    /// <summary>Provisional (S5-11): the 960-wide derivative, per encoding.</summary>
    public const long Medium = 160 * Kibibyte;

    /// <summary>Provisional (S5-11): the 480-wide derivative, per encoding.</summary>
    public const long Small = 60 * Kibibyte;

    /// <summary>Provisional (S5-11): the 1200 × 630 social image.</summary>
    public const long SocialBudget = 250 * Kibibyte;

    /// <summary>The six 3:2 content derivatives, WebP first, each encoding smallest first.</summary>
    public static IReadOnlyList<MediaDerivative> Content { get; } =
    [
        new("480", MediaFormat.WebP, 480, 320, Small),
        new("960", MediaFormat.WebP, 960, 640, Medium),
        new("1600", MediaFormat.WebP, 1600, 1067, Large),
        new("480", MediaFormat.Jpeg, 480, 320, Small),
        new("960", MediaFormat.Jpeg, 960, 640, Medium),
        new("1600", MediaFormat.Jpeg, 1600, 1067, Large),
    ];

    /// <summary>The Open Graph image, published only for items that declare one.</summary>
    public static MediaDerivative Social { get; } = new("og", MediaFormat.Jpeg, 1200, 630, SocialBudget);
}
