using RenoTrack.Website.Site;
using static RenoTrack.Website.Tests.Site.MediaBytes;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Byte-level verification of published derivatives (<b>D106</b>, S5-3): the exact format and size, and no metadata of
/// any kind. Each refusal is proven against an otherwise valid file produced by the real preparation tool.
/// </summary>
public sealed class ImageFileInspectorTests
{
    private static void AssertRefused(byte[] bytes, ImageRefusal refusal)
    {
        var inspection = ImageFileInspector.Inspect(bytes);

        Assert.False(inspection.IsAcceptable);
        Assert.Equal(refusal, inspection.Refusal);
    }

    // ---- Accepted ------------------------------------------------------------------------------

    [Theory]
    [InlineData("testbild-startseite-480.jpg", MediaFormat.Jpeg, 480, 320)]
    [InlineData("testbild-startseite-960.jpg", MediaFormat.Jpeg, 960, 640)]
    [InlineData("testbild-startseite-1600.jpg", MediaFormat.Jpeg, 1600, 1067)]
    [InlineData("testbild-startseite-og.jpg", MediaFormat.Jpeg, 1200, 630)]
    [InlineData("testbild-startseite-480.webp", MediaFormat.WebP, 480, 320)]
    [InlineData("testbild-startseite-960.webp", MediaFormat.WebP, 960, 640)]
    [InlineData("testbild-startseite-1600.webp", MediaFormat.WebP, 1600, 1067)]
    public void A_derivative_from_the_preparation_tool_is_accepted_with_its_exact_size(string file, MediaFormat format, int width, int height)
    {
        var inspection = ImageFileInspector.Inspect(Fixture(file));

        Assert.True(inspection.IsAcceptable, $"{inspection.Refusal}: {inspection.Detail}");
        Assert.Equal((format, width, height), (inspection.Format!.Value, inspection.Width, inspection.Height));
    }

    [Fact]
    public void An_icc_colour_profile_is_image_data_and_accepted_in_a_jpeg()
    {
        var bytes = WithJpegSegment(Jpeg, 0xE2, [.. Ascii("ICC_PROFILE\0"), 1, 1, 0, 0, 0, 0]);

        Assert.True(ImageFileInspector.Inspect(bytes).IsAcceptable);
    }

    [Fact]
    public void A_lossless_webp_header_is_read_for_its_size()
    {
        // VP8L: signature 0x2F, then 14 bits (width − 1) and 14 bits (height − 1), little-endian.
        var bits = (uint)(479 | (319 << 14));
        byte[] header = [0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24), 0];

        var inspection = ImageFileInspector.Inspect(Riff(Chunk("VP8L", header)));

        Assert.True(inspection.IsAcceptable);
        Assert.Equal((480, 320), (inspection.Width, inspection.Height));
    }

    [Fact]
    public void An_extended_webp_with_no_metadata_flags_is_accepted()
    {
        Assert.True(ImageFileInspector.Inspect(ExtendedWebP(WebP, flags: 0)).IsAcceptable);
    }

    // ---- JPEG metadata -----------------------------------------------------------------------------

    [Fact]
    public void Exif_with_gps_is_refused()
    {
        AssertRefused(WithJpegSegment(Jpeg, 0xE1, ExifPayload()), ImageRefusal.Metadata);
    }

    [Fact]
    public void Xmp_is_refused()
    {
        AssertRefused(WithJpegSegment(Jpeg, 0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>")), ImageRefusal.Metadata);
    }

    [Theory]
    [InlineData(0xE3)]
    [InlineData(0xED)]
    [InlineData(0xEE)]
    [InlineData(0xEF)]
    public void Any_application_segment_from_app3_to_app15_is_refused(byte marker)
    {
        AssertRefused(WithJpegSegment(Jpeg, marker, Ascii("Photoshop 3.0\08BIM")), ImageRefusal.Metadata);
    }

    [Fact]
    public void A_comment_is_refused()
    {
        AssertRefused(WithJpegSegment(Jpeg, 0xFE, Ascii("Aufgenommen bei Kunde Muster")), ImageRefusal.Metadata);
    }

    [Fact]
    public void An_app0_segment_other_than_jfif_is_refused()
    {
        AssertRefused(WithJpegSegment(Jpeg, 0xE0, [.. Ascii("JFXX\0"), 0x10, 0, 0]), ImageRefusal.Metadata);
    }

    [Fact]
    public void An_app2_segment_other_than_an_icc_profile_is_refused()
    {
        AssertRefused(WithJpegSegment(Jpeg, 0xE2, Ascii("MPF\0II*\0")), ImageRefusal.Metadata);
    }

    [Fact]
    public void Metadata_after_the_first_scan_is_refused_too()
    {
        var eoi = Jpeg.Length - 2;
        byte[] comment = [0xFF, 0xFE, 0x00, 0x06, .. Ascii("Spät")];

        AssertRefused([.. Jpeg[..eoi], .. comment, .. Jpeg[eoi..]], ImageRefusal.Metadata);
    }

    [Fact]
    public void Bytes_after_the_end_of_image_are_refused()
    {
        AssertRefused([.. Jpeg, .. Ascii("GPS 49.97,6.93")], ImageRefusal.TrailingData);
    }

    [Fact]
    public void A_truncated_jpeg_is_refused()
    {
        AssertRefused(Jpeg[..(Jpeg.Length / 2)], ImageRefusal.Truncated);
    }

    [Fact]
    public void A_lossless_or_arithmetic_jpeg_is_refused()
    {
        var bytes = (byte[])Jpeg.Clone();
        var sof = bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xC0 });
        Assert.True(sof > 0);
        bytes[sof + 1] = 0xC3;

        AssertRefused(bytes, ImageRefusal.Unsupported);
    }

    // ---- WebP metadata -------------------------------------------------------------------------------

    [Fact]
    public void A_webp_declaring_exif_is_refused()
    {
        AssertRefused(ExtendedWebP(WebP, flags: 0x08, ("EXIF", ExifPayload())), ImageRefusal.Metadata);
    }

    [Fact]
    public void A_webp_declaring_xmp_is_refused()
    {
        AssertRefused(ExtendedWebP(WebP, flags: 0x04, ("XMP ", Ascii("<x:xmpmeta/>"))), ImageRefusal.Metadata);
    }

    /// <summary>The flags are a claim; the chunks are the fact. An undeclared metadata chunk is refused as well.</summary>
    [Theory]
    [InlineData("EXIF")]
    [InlineData("XMP ")]
    public void An_undeclared_metadata_chunk_is_refused(string fourCc)
    {
        AssertRefused(ExtendedWebP(WebP, flags: 0, (fourCc, Ascii("GPS 49.97,6.93"))), ImageRefusal.Metadata);
    }

    [Fact]
    public void An_animated_webp_is_refused()
    {
        AssertRefused(ExtendedWebP(WebP, flags: 0x02), ImageRefusal.Unsupported);
    }

    [Fact]
    public void An_unknown_chunk_is_refused()
    {
        AssertRefused(ExtendedWebP(WebP, flags: 0, ("ABCD", Ascii("verborgen"))), ImageRefusal.Unsupported);
    }

    [Fact]
    public void A_colour_profile_chunk_outside_the_extended_format_is_refused()
    {
        AssertRefused(Riff([.. WebP[12..], .. Chunk("ICCP", Ascii("profil"))]), ImageRefusal.Unsupported);
    }

    [Fact]
    public void Two_bitstreams_are_refused()
    {
        AssertRefused(Riff([.. WebP[12..], .. WebP[12..]]), ImageRefusal.Malformed);
    }

    [Fact]
    public void A_riff_size_that_does_not_match_the_file_is_refused()
    {
        AssertRefused([.. WebP, 0, 0], ImageRefusal.TrailingData);
        AssertRefused(WebP[..^4], ImageRefusal.Truncated);
    }

    // ---- Not an image ---------------------------------------------------------------------------------

    [Fact]
    public void Anything_but_a_jpeg_or_webp_signature_is_refused()
    {
        AssertRefused([0x89, .. Ascii("PNG\r\n\n"), 0, 0, 0, 0], ImageRefusal.UnrecognisedFormat);
        AssertRefused(Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), ImageRefusal.UnrecognisedFormat);
        AssertRefused([], ImageRefusal.UnrecognisedFormat);
    }
}
