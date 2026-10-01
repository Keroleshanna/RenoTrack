using RenoTrack.Website.Site;
using SkiaSharp;

namespace RenoTrack.MediaPrep;

/// <summary>One written derivative and how it measured against its budget.</summary>
public sealed record PreparedFile(string FileName, MediaDerivative Derivative, long Bytes)
{
    public bool IsWithinBudget => Bytes <= Derivative.MaxBytes;
}

/// <summary>
/// Turns one source photo into its published derivatives (<b>D106</b>, S5-4, S5-11).
/// </summary>
/// <remarks>
/// <para><b>The encoding settings are fixed constants.</b> Changing any of them is an ADR change and repeats the real-media
/// budget check in <c>MEDIA_PREPARATION.md</c>. The same source, crop specification and tool version produce
/// byte-identical files.</para>
/// <list type="number">
/// <item>Decode to sRGB, then apply the EXIF orientation — before anything else, because stripping metadata without
/// applying orientation publishes a phone photo on its side.</item>
/// <item>Crop to the operator's explicit rectangle. There is no automatic cropping.</item>
/// <item>Downscale only: deterministic 2× box halvings (bilinear at exactly one half) while the image is at least twice
/// the target, then one Mitchell cubic resample to the exact size. The halvings keep fine repeating detail — tile
/// joints — from aliasing, which a single cubic step over a large reduction would do.</item>
/// <item>Encode WebP (lossy, quality <see cref="WebPQuality"/>) and JPEG (quality <see cref="JpegQuality"/>, 4:2:0,
/// baseline) from pixels with no colour space attached, so no ICC profile and no metadata of any kind is written.</item>
/// <item>Verify every file with the Website's own <see cref="ImageFileInspector"/> and the exact size.</item>
/// </list>
/// <para><b>It never fits a budget.</b> An over-budget file is written and reported; the operator decides, globally, per
/// <c>MEDIA_PREPARATION.md</c>. Quality is never lowered for one photo.</para>
/// </remarks>
public static class MediaPipeline
{
    /// <summary>Lossy WebP quality. Provisional until the real-media validation (S5-11).</summary>
    public const int WebPQuality = 80;

    /// <summary>JPEG quality. Provisional until the real-media validation (S5-11).</summary>
    public const int JpegQuality = 82;

    /// <summary>Prepares one item into <paramref name="outputDirectory"/>. Refuses to overwrite any existing file.</summary>
    /// <exception cref="MediaPrepException">The source is missing or unreadable, a crop is out of bounds, or a file exists.</exception>
    public static IReadOnlyList<PreparedFile> Prepare(CropSpecItem item, string sourceDirectory, string outputDirectory)
    {
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var sourcePath = Path.GetFullPath(Path.Combine(sourceRoot, item.Source));
        if (!sourcePath.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(sourcePath))
        {
            throw new MediaPrepException($"Item '{item.Id}': the source file does not exist in the source directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        var targets = MediaDerivatives.Content
            .Select(derivative => (Derivative: derivative, Crop: item.Crop))
            .Concat(item.SocialCrop is null ? [] : [(MediaDerivatives.Social, item.SocialCrop)])
            .ToList();

        // Refuse before writing anything: a replacement is a new id, never an overwritten file.
        foreach (var (derivative, _) in targets)
        {
            if (File.Exists(Path.Combine(outputDirectory, derivative.FileNameFor(item.Id))))
            {
                throw new MediaPrepException(
                    $"Item '{item.Id}': '{derivative.FileNameFor(item.Id)}' already exists. A replacement photo gets a new id.");
            }
        }

        using var oriented = DecodeOriented(sourcePath, item.Id);

        var prepared = new List<PreparedFile>();
        foreach (var (derivative, crop) in targets)
        {
            if (crop.X + (long)crop.Width > oriented.Width || crop.Y + (long)crop.Height > oriented.Height)
            {
                throw new MediaPrepException(
                    $"Item '{item.Id}': the {derivative.Suffix} crop extends past the {oriented.Width} × {oriented.Height} source.");
            }

            using var resized = CropAndResize(oriented, crop, derivative.Width, derivative.Height);
            var bytes = Encode(resized, derivative.Format);

            var inspection = ImageFileInspector.Inspect(bytes);
            if (!inspection.IsAcceptable || inspection.Format != derivative.Format
                || inspection.Width != derivative.Width || inspection.Height != derivative.Height)
            {
                throw new MediaPrepException(
                    $"Item '{item.Id}': the encoded {derivative.FileNameFor(item.Id)} failed verification " +
                    $"({inspection.Refusal}, {inspection.Width} × {inspection.Height}). This is a tool defect.");
            }

            var fileName = derivative.FileNameFor(item.Id);
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), bytes);
            prepared.Add(new PreparedFile(fileName, derivative, bytes.Length));
        }

        return prepared;
    }

    /// <summary>Decodes to 8-bit sRGB and applies the EXIF orientation, returning an upright bitmap.</summary>
    internal static SKBitmap DecodeOriented(string path, string id)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new MediaPrepException($"Item '{id}': the source is not a supported image (JPEG or PNG; convert HEIC first).");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new MediaPrepException($"Item '{id}': the source could not be decoded completely ({result}).");
        }

        return ApplyOrigin(decoded, codec.EncodedOrigin);
    }

    /// <summary>The eight EXIF orientations as exact integer pixel mappings (no resampling).</summary>
    internal static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        int w = source.Width, h = source.Height;
        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var info = new SKImageInfo(swaps ? h : w, swaps ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul, source.ColorSpace);

        // x' = ScaleX·x + SkewX·y + TransX;  y' = SkewY·x + ScaleY·y + TransY
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var upright = new SKBitmap(info);
        using var canvas = new SKCanvas(upright);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        canvas.Flush();
        return upright;
    }

    internal static SKBitmap CropAndResize(SKBitmap source, CropRect crop, int width, int height)
    {
        var current = new SKBitmap();
        if (!source.ExtractSubset(current, new SKRectI(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height)))
        {
            throw new MediaPrepException("The crop rectangle could not be extracted.");
        }

        // A subset shares pixels with its parent; copy so the halvings own their memory.
        var owned = current.Copy();
        current.Dispose();
        current = owned;

        while (current.Width / 2 >= width && current.Height / 2 >= height)
        {
            var half = current.Resize(
                new SKImageInfo(current.Width / 2, current.Height / 2, SKColorType.Rgba8888, SKAlphaType.Premul, current.ColorSpace),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            current.Dispose();
            current = half;
        }

        var target = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, current.ColorSpace);
        var resized = current.Resize(target, new SKSamplingOptions(SKCubicResampler.Mitchell));
        current.Dispose();
        return resized;
    }

    /// <summary>Encodes from opaque pixels with no colour space attached, so no ICC profile or metadata is written.</summary>
    internal static byte[] Encode(SKBitmap bitmap, MediaFormat format)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque, null);
        using var opaque = new SKBitmap(info);
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        using var pixmap = opaque.PeekPixels();
        using var data = format == MediaFormat.WebP
            ? pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebPQuality))
            : pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore));

        return data?.ToArray() ?? throw new MediaPrepException($"The {format} encoder produced no output.");
    }
}
