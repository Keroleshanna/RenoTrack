using RenoTrack.Website.Site;
using SkiaSharp;

namespace RenoTrack.MediaPrep.Tests;

/// <summary>
/// The offline preparation tool (<b>D106</b>, S5-4): every derivative it writes is one the Website accepts, its output
/// is reproducible, orientation is applied before metadata is dropped, and it never fits a budget by itself.
/// Synthetic test graphics only — no real photo enters this repository.
/// </summary>
public sealed class MediaPipelineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"renotrack-mediaprep-{Guid.NewGuid():N}");

    public MediaPipelineTests()
    {
        Directory.CreateDirectory(Source);
    }

    private string Source => Path.Combine(root, "source");

    private string Output(string name = "out") => Path.Combine(root, name);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string WritePatternPng(string fileName, int width = 2400, int height = 1600, int palette = 0)
    {
        using var bitmap = SyntheticImages.Pattern(width, height, palette);
        File.WriteAllBytes(Path.Combine(Source, fileName), SyntheticImages.EncodePng(bitmap));
        return fileName;
    }

    private static CropSpecItem Item(string id, string source, bool social = true, CropRect? crop = null) =>
        new(id, source, crop ?? new CropRect(0, 0, 2400, 1600), social ? new CropRect(0, 0, 2400, 1260) : null);

    // ---- Output the Website accepts ------------------------------------------------------------

    [Fact]
    public void Every_derivative_is_written_with_its_exact_size_and_passes_the_websites_inspector()
    {
        var source = WritePatternPng("muster.png");

        var prepared = MediaPipeline.Prepare(Item("testbild-muster", source), Source, Output());

        Assert.Equal(7, prepared.Count);
        foreach (var derivative in MediaDerivatives.Content.Append(MediaDerivatives.Social))
        {
            var path = Path.Combine(Output(), derivative.FileNameFor("testbild-muster"));
            Assert.True(File.Exists(path), $"{derivative.FileNameFor("testbild-muster")} is missing");

            var inspection = ImageFileInspector.Inspect(File.ReadAllBytes(path));
            Assert.True(inspection.IsAcceptable, $"{path}: {inspection.Refusal} {inspection.Detail}");
            Assert.Equal(derivative.Format, inspection.Format);
            Assert.Equal((derivative.Width, derivative.Height), (inspection.Width, inspection.Height));
        }
    }

    [Fact]
    public void Without_a_social_crop_no_social_image_is_written()
    {
        var source = WritePatternPng("muster.png");

        var prepared = MediaPipeline.Prepare(Item("testbild-muster", source, social: false), Source, Output());

        Assert.Equal(6, prepared.Count);
        Assert.False(File.Exists(Path.Combine(Output(), "testbild-muster-og.jpg")));
    }

    [Fact]
    public void The_same_source_and_specification_produce_byte_identical_files()
    {
        var source = WritePatternPng("muster.png");

        MediaPipeline.Prepare(Item("testbild-muster", source), Source, Output("first"));
        MediaPipeline.Prepare(Item("testbild-muster", source), Source, Output("second"));

        foreach (var file in Directory.EnumerateFiles(Output("first")))
        {
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(Output("second"), Path.GetFileName(file))));
        }
    }

    /// <summary>The encoder writes no colour profile either: nothing beyond the pixels leaves the tool.</summary>
    [Fact]
    public void Output_carries_no_icc_profile_or_metadata_of_any_kind()
    {
        var source = WritePatternPng("muster.png");
        MediaPipeline.Prepare(Item("testbild-muster", source), Source, Output());

        foreach (var file in Directory.EnumerateFiles(Output()))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(Contains(bytes, "ICC_PROFILE"u8), $"{file} carries an ICC profile");
            Assert.False(Contains(bytes, "ICCP"u8), $"{file} carries an ICC chunk");
            Assert.False(Contains(bytes, "Exif"u8), $"{file} carries EXIF");
            Assert.False(Contains(bytes, "http://ns.adobe.com/xap"u8), $"{file} carries XMP");
        }
    }

    // ---- Orientation and location ---------------------------------------------------------------

    /// <summary>
    /// A phone stores a landscape shot sideways and records "rotate 90° clockwise" in EXIF. Dropping EXIF without
    /// applying it would publish the photo on its side; applying it puts the stored top-left corner at the top right.
    /// </summary>
    [Fact]
    public void Exif_orientation_is_applied_before_metadata_is_dropped()
    {
        using var stored = new SKBitmap(new SKImageInfo(1600, 2400, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(stored))
        {
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(0, 0, 400, 400), red);
        }

        var jpeg = Exif.WithOrientationAndGps(SyntheticImages.EncodeJpeg(stored), orientation: 6);
        File.WriteAllBytes(Path.Combine(Source, "hochkant.jpg"), jpeg);

        MediaPipeline.Prepare(Item("testbild-gedreht", "hochkant.jpg", social: false), Source, Output());

        using var result = SKBitmap.Decode(Path.Combine(Output(), "testbild-gedreht-1600.jpg"));
        Assert.Equal((1600, 1067), (result.Width, result.Height));
        var topRight = result.GetPixel(1600 - 40, 40);
        var topLeft = result.GetPixel(40, 40);
        Assert.True(topRight.Red > 200 && topRight.Green < 80, $"top right should be red, was {topRight}");
        Assert.True(topLeft.Green > 200, $"top left should be white, was {topLeft}");
    }

    [Fact]
    public void Gps_coordinates_in_the_source_never_reach_a_derivative()
    {
        using var bitmap = SyntheticImages.Pattern(2400, 1600, palette: 1);
        var jpeg = Exif.WithOrientationAndGps(SyntheticImages.EncodeJpeg(bitmap), orientation: 1);
        Assert.Equal(ImageRefusal.Metadata, ImageFileInspector.Inspect(jpeg).Refusal);
        File.WriteAllBytes(Path.Combine(Source, "mit-gps.jpg"), jpeg);

        MediaPipeline.Prepare(Item("testbild-gps", "mit-gps.jpg"), Source, Output());

        foreach (var file in Directory.EnumerateFiles(Output()))
        {
            Assert.True(ImageFileInspector.Inspect(File.ReadAllBytes(file)).IsAcceptable);
            Assert.False(Contains(File.ReadAllBytes(file), "Exif"u8));
        }
    }

    // ---- Budgets: reported, never fitted ------------------------------------------------------------

    [Fact]
    public void An_over_budget_file_is_written_and_reported_at_the_fixed_quality_never_re_encoded_to_fit()
    {
        using var noise = SyntheticImages.Noise(2400, 1600, seed: 5);
        File.WriteAllBytes(Path.Combine(Source, "rauschen.png"), SyntheticImages.EncodePng(noise));

        var prepared = MediaPipeline.Prepare(Item("testbild-rauschen", "rauschen.png", social: false), Source, Output());

        var large = prepared.Single(file => file.FileName == "testbild-rauschen-1600.jpg");
        Assert.False(large.IsWithinBudget);
        Assert.Equal(large.Bytes, new FileInfo(Path.Combine(Output(), large.FileName)).Length);
        Assert.Equal(82, MediaPipeline.JpegQuality);
        Assert.Equal(80, MediaPipeline.WebPQuality);
    }

    // ---- Refusals ------------------------------------------------------------------------------------

    [Fact]
    public void An_existing_output_file_is_never_overwritten_and_nothing_is_written()
    {
        var source = WritePatternPng("muster.png");
        Directory.CreateDirectory(Output());
        File.WriteAllText(Path.Combine(Output(), "testbild-muster-1600.jpg"), "vorhanden");

        var refusal = Assert.Throws<MediaPrepException>(() => MediaPipeline.Prepare(Item("testbild-muster", source), Source, Output()));

        Assert.Contains("new id", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("vorhanden", File.ReadAllText(Path.Combine(Output(), "testbild-muster-1600.jpg")));
        Assert.Single(Directory.EnumerateFiles(Output()));
    }

    [Fact]
    public void A_crop_past_the_source_is_refused()
    {
        var source = WritePatternPng("muster.png", 2000, 1334);

        Assert.Throws<MediaPrepException>(() => MediaPipeline.Prepare(Item("testbild-muster", source, social: false), Source, Output()));
    }

    [Fact]
    public void A_missing_source_is_refused()
    {
        Assert.Throws<MediaPrepException>(() => MediaPipeline.Prepare(Item("testbild-fehlt", "fehlt.png"), Source, Output()));
    }

    public static TheoryData<string, string> InvalidSpecifications() => new()
    {
        { "Id", """{ "Items": [ { "Id": "Kein Slug", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
        { "Source", """{ "Items": [ { "Id": "a", "Source": "../a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
        { "Source", """{ "Items": [ { "Id": "a", "Source": "sub\\a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
        { "Crop", """{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1800 } } ] }""" },
        { "Crop", """{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 1200, "Height": 800 } } ] }""" },
        { "Crop", """{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": -1, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
        { "SocialCrop", """{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 }, "SocialCrop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
        { "Id", """{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } }, { "Id": "a", "Source": "b.png", "Crop": { "X": 0, "Y": 0, "Width": 2400, "Height": 1600 } } ] }""" },
    };

    [Theory]
    [MemberData(nameof(InvalidSpecifications))]
    public void A_malformed_specification_is_refused_naming_the_field(string field, string json)
    {
        var refusal = Assert.Throws<MediaPrepException>(() => CropSpec.Parse(json).Validate());

        Assert.Contains(field, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_pixel_rounding_difference_in_the_aspect_ratio_is_accepted()
    {
        CropSpec.Parse("""{ "Items": [ { "Id": "a", "Source": "a.png", "Crop": { "X": 0, "Y": 0, "Width": 1601, "Height": 1067 } } ] }""").Validate();
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
