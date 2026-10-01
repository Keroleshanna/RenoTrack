using System.Text.RegularExpressions;

namespace RenoTrack.Website.Content;

/// <summary>
/// One approved company photo as the site publishes it, bound from <c>Site:Media</c> (Phase 13 Slice 5a,
/// <b>D106</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The pack lists finished derivatives, never source photos.</b> The files themselves sit in the pack's
/// <c>media/</c> directory as <c>{Id}-{480|960|1600}.{webp|jpg}</c> (and <c>{Id}-og.jpg</c>), produced offline by
/// <c>tools/RenoTrack.MediaPrep</c> and verified byte by byte at startup. A replacement photo is a new
/// <see cref="Id"/>, never an overwritten file.
/// </para>
/// <para>
/// <b><see cref="SourceRef"/> is traceability, not approval (S5-9).</b> It points into the company's private
/// approval register — who approved, consent, the privacy checklist — which never enters any repository. The
/// application checks its shape, never renders or logs it, and cannot know whether the approval is real.
/// </para>
/// <para>
/// <b><see cref="Caption"/> is validated now and first rendered by the projects gallery (Slice 5b).</b> Hero and
/// service images carry no caption.
/// </para>
/// </remarks>
public sealed partial class MediaItemOptions
{
    internal const int MaxIdLength = 60;
    internal const int MaxAltLength = 150;
    internal const int MaxCaptionLength = 200;
    internal const int MaxSourceRefLength = 40;

    /// <summary>Descriptive, lowercase ASCII with single hyphens; the derivative file names are built from it.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Describes the actual image for accessibility, and also gives search engines and other machine consumers
    /// useful context. It is not a keyword field.
    /// </summary>
    public string? Alt { get; init; }

    public string? Caption { get; init; }

    /// <summary>An opaque reference into the private approval register. Never rendered, never logged.</summary>
    public string? SourceRef { get; init; }

    /// <summary>Whether <c>{Id}-og.jpg</c> exists, so a page using this image may declare it as its <c>og:image</c>.</summary>
    public bool HasSocialImage { get; init; }

    internal static bool IsValidId(string? value) =>
        !ContentText.IsBlank(value) && value!.Length <= MaxIdLength && IdPattern().IsMatch(value);

    internal void Validate(string path)
    {
        if (!IsValidId(Id))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Id)}' must be lowercase ASCII letters and digits separated by single " +
                $"hyphens, at most {MaxIdLength} characters.");
        }

        ContentText.ValidateRequired(Alt, $"{path}:{nameof(Alt)}", MaxAltLength);
        ContentText.ValidateOptional(Caption, $"{path}:{nameof(Caption)}", MaxCaptionLength);

        // A caption equal to the alt text would be announced twice by a screen reader and adds nothing on screen.
        if (!ContentText.IsBlank(Caption)
            && string.Equals(Caption!.Trim(), Alt!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Caption)}' repeats '{path}:{nameof(Alt)}'. A caption adds information " +
                "the alternative text does not; remove it or change it.");
        }

        if (ContentText.IsBlank(SourceRef) || SourceRef!.Length > MaxSourceRefLength || !SourceRefPattern().IsMatch(SourceRef))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(SourceRef)}' is required: a reference into the private approval " +
                $"register of lowercase ASCII letters, digits and hyphens, at most {MaxSourceRefLength} characters.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SourceRefPattern();
}
