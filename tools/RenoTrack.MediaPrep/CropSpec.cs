using System.Text.Json;
using System.Text.RegularExpressions;
using RenoTrack.Website.Site;

namespace RenoTrack.MediaPrep;

/// <summary>A rectangle in the source photo's pixels, after its EXIF orientation has been applied.</summary>
public sealed record CropRect(int X, int Y, int Width, int Height);

/// <summary>One media item to prepare: which source, which explicit crops, which id to publish it under.</summary>
/// <param name="Source">A file name inside the source directory — never a path.</param>
/// <param name="Crop">The 3:2 content crop.</param>
/// <param name="SocialCrop">The 1200:630 Open Graph crop, or <c>null</c> for no social image.</param>
public sealed record CropSpecItem(string Id, string Source, CropRect Crop, CropRect? SocialCrop);

/// <summary>
/// The crop specification an operator writes in the private content repository (<b>D106</b>). It holds file names
/// and rectangles only; approval, consent and provenance live in the private register, not here.
/// </summary>
public sealed partial record CropSpec(IReadOnlyList<CropSpecItem> Items)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
    };

    public static CropSpec Parse(string json) =>
        JsonSerializer.Deserialize<CropSpec>(json, JsonOptions)
        ?? throw new MediaPrepException("The crop specification is empty.");

    /// <summary>Shape checks that need no image: ids, source names, rectangle aspect and size. Bounds come later.</summary>
    /// <exception cref="MediaPrepException">The first problem found, naming the item's position.</exception>
    public void Validate()
    {
        if (Items is null || Items.Count == 0)
        {
            throw new MediaPrepException("The crop specification lists no items.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            var at = $"Items[{index}]";

            if (item.Id is null || item.Id.Length > 60 || !IdPattern().IsMatch(item.Id))
            {
                throw new MediaPrepException($"{at}.Id must be lowercase ASCII letters and digits separated by single hyphens, at most 60 characters.");
            }

            if (!ids.Add(item.Id))
            {
                throw new MediaPrepException($"{at}.Id repeats an earlier item's id.");
            }

            if (string.IsNullOrWhiteSpace(item.Source)
                || item.Source != Path.GetFileName(item.Source)
                || item.Source.Contains('\\', StringComparison.Ordinal)
                || item.Source is "." or "..")
            {
                throw new MediaPrepException($"{at}.Source must be a file name inside the source directory, not a path.");
            }

            ValidateRect(item.Crop, $"{at}.Crop", MediaDerivatives.Content[^1]);

            if (item.SocialCrop is not null)
            {
                ValidateRect(item.SocialCrop, $"{at}.SocialCrop", MediaDerivatives.Social);
            }
        }
    }

    /// <summary>
    /// The crop must have the target's aspect ratio (to within one pixel of rounding) and be at least as large as the
    /// largest derivative, because derivatives are only ever downscaled.
    /// </summary>
    private static void ValidateRect(CropRect? rect, string at, MediaDerivative largest)
    {
        if (rect is null || rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new MediaPrepException($"{at} must be a rectangle with a non-negative origin and a positive size.");
        }

        var expectedHeight = (long)Math.Round(rect.Width * (double)largest.Height / largest.Width, MidpointRounding.AwayFromZero);
        if (Math.Abs(rect.Height - expectedHeight) > 1)
        {
            throw new MediaPrepException(
                $"{at} is {rect.Width} × {rect.Height}, which is not the {largest.Width}:{largest.Height} aspect ratio " +
                $"(expected a height of {expectedHeight} for that width).");
        }

        if (rect.Width < largest.Width)
        {
            throw new MediaPrepException(
                $"{at} is {rect.Width} pixels wide, narrower than the {largest.Width}-pixel derivative. Photos are never enlarged.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}

/// <summary>A refusal the operator can act on. Messages name positions and rules, never file content.</summary>
public sealed class MediaPrepException(string message) : Exception(message);
