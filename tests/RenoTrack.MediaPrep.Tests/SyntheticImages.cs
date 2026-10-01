using SkiaSharp;

namespace RenoTrack.MediaPrep.Tests;

/// <summary>
/// Synthetic source images: plainly test graphics — colour bands, a grid and diagonals — never anything that could be
/// mistaken for a photograph of work (D106). They exercise the real pipeline without a single real photo.
/// </summary>
internal static class SyntheticImages
{
    /// <summary>
    /// A deterministic test pattern with repeating lines, enough to exercise downscaling. Deliberately not dense enough to
    /// say anything about byte budgets: those are validated against real approved photos only (S5-11).
    /// </summary>
    internal static SKBitmap Pattern(int width, int height, int palette)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);

        SKColor[][] palettes =
        [
            [new(0x1F, 0x4B, 0x7A), new(0xE8, 0xC2, 0x7A), new(0xF5, 0xF3, 0xEF), new(0x4F, 0x5A, 0x66)],
            [new(0x2E, 0x6B, 0x4F), new(0xD9, 0xA4, 0x41), new(0xEE, 0xEE, 0xEE), new(0x5B, 0x3A, 0x29)],
            [new(0x6A, 0x2C, 0x70), new(0x9C, 0xC6, 0xD9), new(0xFA, 0xFA, 0xF5), new(0x33, 0x33, 0x33)],
        ];
        var colours = palettes[palette % palettes.Length];

        var bands = 8;
        using var fill = new SKPaint { IsAntialias = false };
        for (var band = 0; band < bands; band++)
        {
            fill.Color = colours[band % colours.Length];
            canvas.DrawRect(new SKRect(band * width / (float)bands, 0, (band + 1) * width / (float)bands, height), fill);
        }

        using var line = new SKPaint { IsAntialias = true, Color = new SKColor(0x1C, 0x21, 0x28), StrokeWidth = 4 };
        for (var x = 0; x < width; x += 160)
        {
            canvas.DrawLine(x, 0, x, height, line);
        }

        for (var y = 0; y < height; y += 160)
        {
            canvas.DrawLine(0, y, width, y, line);
        }

        using var diagonal = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = 12 };
        canvas.DrawLine(0, 0, width, height, diagonal);
        canvas.DrawLine(width, 0, 0, height, diagonal);

        return bitmap;
    }

    /// <summary>Seeded noise: incompressible on purpose, to produce derivatives over their budgets.</summary>
    internal static SKBitmap Noise(int width, int height, int seed)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    internal static byte[] EncodePng(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions());
        return data!.ToArray();
    }

    internal static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 95)
    {
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore));
        return data!.ToArray();
    }
}

/// <summary>
/// Builds EXIF APP1 segments the way a phone writes them, so tests can prove the pipeline applies orientation and
/// drops location data. Little-endian TIFF, IFD0 with Orientation and a GPS IFD pointer, and a GPS IFD with latitude.
/// </summary>
internal static class Exif
{
    /// <summary>A JPEG with an EXIF segment carrying <paramref name="orientation"/> and GPS coordinates, inserted after SOI.</summary>
    internal static byte[] WithOrientationAndGps(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        void U16(int value) { tiff.Add((byte)value); tiff.Add((byte)(value >> 8)); }
        void U32(int value) { U16(value & 0xFFFF); U16((value >> 16) & 0xFFFF); }

        // Header: "II", 42, offset of IFD0 = 8.
        tiff.AddRange("II"u8.ToArray());
        U16(42);
        U32(8);

        // IFD0 at 8: two entries (12 bytes each) + next-IFD offset → ends at 8 + 2 + 24 + 4 = 38.
        const int gpsIfdOffset = 38;
        U16(2);
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);   // Orientation, SHORT
        U16(0x8825); U16(4); U32(1); U32(gpsIfdOffset);          // GPS IFD pointer, LONG
        U32(0);

        // GPS IFD at 38: GPSLatitudeRef 'N' (ASCII, inline) and GPSLatitude (3 RATIONAL, at 38 + 2 + 24 + 4 = 68).
        const int latitudeOffset = 68;
        U16(2);
        U16(0x0001); U16(2); U32(2); tiff.Add((byte)'N'); tiff.Add(0); U16(0);
        U16(0x0002); U16(5); U32(3); U32(latitudeOffset);
        U32(0);
        foreach (var (numerator, denominator) in new[] { (49, 1), (58, 1), (1234, 100) })
        {
            U32(numerator);
            U32(denominator);
        }

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);

        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }
}
