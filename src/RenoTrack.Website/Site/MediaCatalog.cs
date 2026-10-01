using System.Security.Cryptography;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>One published derivative file, verified at startup.</summary>
/// <param name="Version">16 hex characters of the file's SHA-256, appended to every URL as <c>?v=</c> (S5-12).</param>
public sealed record MediaFile(string FileName, string AbsolutePath, string ContentType, long Length, string Version)
{
    /// <summary>The site-relative URL, e.g. <c>/medien/badezimmer-01-960.webp?v=0123456789abcdef</c>.</summary>
    public string Url => $"{MediaCatalog.RequestPath}/{FileName}?v={Version}";
}

/// <summary>A media item as pages render it: its text and its verified derivatives.</summary>
public sealed class MediaImage
{
    internal MediaImage(MediaItemOptions item, IReadOnlyList<MediaFile> webP, IReadOnlyList<MediaFile> jpeg, MediaFile? social)
    {
        Id = item.Id!;
        Alt = item.Alt!.Trim();
        Caption = string.IsNullOrWhiteSpace(item.Caption) ? null : item.Caption.Trim();
        WebP = webP;
        Jpeg = jpeg;
        Social = social;
    }

    public string Id { get; }

    public string Alt { get; }

    public string? Caption { get; }

    /// <summary>480, 960, 1600 wide, in that order.</summary>
    public IReadOnlyList<MediaFile> WebP { get; }

    /// <summary>480, 960, 1600 wide, in that order.</summary>
    public IReadOnlyList<MediaFile> Jpeg { get; }

    /// <summary>The 1200 × 630 Open Graph image, or <c>null</c> when the item declares none.</summary>
    public MediaFile? Social { get; }

    /// <summary>The intrinsic size every <c>&lt;img&gt;</c> declares: the largest derivative's.</summary>
    public int Width => MediaDerivatives.Content[^1].Width;

    public int Height => MediaDerivatives.Content[^1].Height;

    public string WebPSrcset => Srcset(WebP, MediaFormat.WebP);

    public string JpegSrcset => Srcset(Jpeg, MediaFormat.Jpeg);

    /// <summary>The <c>src</c> for browsers without <c>srcset</c>: the 960-wide JPEG.</summary>
    public string FallbackUrl => Jpeg[1].Url;

    private static string Srcset(IReadOnlyList<MediaFile> files, MediaFormat format)
    {
        var widths = MediaDerivatives.Content.Where(derivative => derivative.Format == format).ToList();
        return string.Join(", ", files.Select((file, index) => $"{file.Url} {widths[index].Width}w"));
    }
}

/// <summary>
/// Every photo the site may publish, verified once at startup: the only source of what <c>/medien/</c> serves
/// (<b>D106</b>, S5-2, S5-3).
/// </summary>
/// <remarks>
/// <para>
/// <b>An allowlist, not a directory mount.</b> The endpoint looks a requested file name up in this catalog's
/// ordinal dictionary, and nothing else: a request never builds a filesystem path. A source photo copied into
/// <c>media/</c> by mistake, a derivative of an item no longer listed, <c>../site.json</c> or any encoded escape all
/// answer 404, because none of them is a key here.
/// </para>
/// <para>
/// <b>Nothing is published unverified.</b> Every derivative of every listed item must exist, sit under the media
/// directory, stay within its byte budget, be the format its extension says, have exactly its specified pixel size,
/// and carry no metadata (<see cref="ImageFileInspector"/>). Any failure stops startup naming the configuration key
/// and the derivative — a page must never render an image that is missing, oversized or leaking EXIF.
/// </para>
/// <para>
/// <b>Content hashes make immutable caching safe (S5-12).</b> Replacing a photo means a new id, but a file silently
/// overwritten under the same name still gets a new URL, because the hash is computed from its bytes at startup.
/// </para>
/// </remarks>
public sealed class MediaCatalog
{
    /// <summary>The URL path under which derivatives are served.</summary>
    public const string RequestPath = "/medien";

    private readonly Dictionary<string, MediaImage> imagesById;
    private readonly Dictionary<string, MediaFile> filesByName;

    private MediaCatalog(Dictionary<string, MediaImage> imagesById, Dictionary<string, MediaFile> filesByName)
    {
        this.imagesById = imagesById;
        this.filesByName = filesByName;
    }

    /// <summary>No photos: every page keeps its text-only layout and <c>/medien/</c> serves nothing.</summary>
    public static MediaCatalog Empty { get; } = new(new(StringComparer.Ordinal), new(StringComparer.Ordinal));

    public int ImageCount => imagesById.Count;

    public int FileCount => filesByName.Count;

    /// <summary>The verified image with exactly this id, or <c>null</c>.</summary>
    public MediaImage? Image(string? id) =>
        id is not null && imagesById.TryGetValue(id.Trim(), out var image) ? image : null;

    /// <summary>The verified file with exactly this name — ordinal, case-sensitive — or <c>null</c>.</summary>
    public MediaFile? File(string? fileName) =>
        fileName is not null && filesByName.TryGetValue(fileName, out var file) ? file : null;

    /// <summary>Loads and verifies every derivative of every listed item.</summary>
    /// <param name="site">Already validated: ids are well-formed, unique and referenced.</param>
    /// <exception cref="InvalidOperationException">A derivative is missing, oversized, malformed or carries metadata.</exception>
    public static MediaCatalog Load(SiteOptions site, string mediaRoot)
    {
        if (site.Media.Count == 0)
        {
            return Empty;
        }

        var mediaKey = $"{SiteOptions.SectionName}:{nameof(SiteOptions.Media)}";
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"Configuration '{mediaKey}' lists photos, but the media directory '{root}' does not exist. Place the " +
                $"prepared derivatives in the content pack's '{ContentPackOptions.MediaDirectoryName}' directory.");
        }

        var images = new Dictionary<string, MediaImage>(StringComparer.Ordinal);
        var files = new Dictionary<string, MediaFile>(StringComparer.Ordinal);

        for (var index = 0; index < site.Media.Count; index++)
        {
            var item = site.Media[index];
            var key = $"{mediaKey}:{index}";

            var webP = new List<MediaFile>();
            var jpeg = new List<MediaFile>();
            foreach (var derivative in MediaDerivatives.Content)
            {
                var file = LoadDerivative(root, item.Id!, derivative, key);
                (derivative.Format == MediaFormat.WebP ? webP : jpeg).Add(file);
                files.Add(file.FileName, file);
            }

            MediaFile? social = null;
            if (item.HasSocialImage)
            {
                social = LoadDerivative(root, item.Id!, MediaDerivatives.Social, key);
                files.Add(social.FileName, social);
            }

            images.Add(item.Id!, new MediaImage(item, webP, jpeg, social));
        }

        return new MediaCatalog(images, files);
    }

    private static MediaFile LoadDerivative(string root, string id, MediaDerivative derivative, string key)
    {
        var fileName = derivative.FileNameFor(id);
        var described = $"{derivative.Width} × {derivative.Height} {derivative.Format} derivative '{fileName}'";
        var path = Path.GetFullPath(Path.Combine(root, fileName));

        // Defence in depth: the id pattern already makes an escape impossible.
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configuration '{key}' names a {described} outside the media directory.");
        }

        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has no {described}. Every listed photo needs all of its derivatives, produced " +
                "by tools/RenoTrack.MediaPrep.");
        }

        var length = new FileInfo(path).Length;
        if (length > derivative.MaxBytes)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has a {described} of {length} bytes, over its budget of {derivative.MaxBytes} " +
                "bytes. Do not re-encode this one photo at lower quality; see MEDIA_PREPARATION.md.");
        }

        var bytes = System.IO.File.ReadAllBytes(path);
        var inspection = ImageFileInspector.Inspect(bytes);

        if (!inspection.IsAcceptable)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has a {described} that cannot be published: {inspection.Refusal} " +
                $"({inspection.Detail}).");
        }

        if (inspection.Format != derivative.Format)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has a {described} whose content is {inspection.Format}, not {derivative.Format}.");
        }

        if (inspection.Width != derivative.Width || inspection.Height != derivative.Height)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has a {described} measuring {inspection.Width} × {inspection.Height}.");
        }

        var version = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        return new MediaFile(fileName, path, derivative.ContentType, length, version);
    }
}
