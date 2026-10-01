using RenoTrack.Website.Content;
using RenoTrack.Website.Site;
using static RenoTrack.Website.Tests.Site.MediaBytes;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Startup verification of every listed derivative (<b>D106</b>, S5-3, S5-11, S5-12): nothing is published that is
/// missing, oversized, the wrong format or size, or carries metadata — and every failure names the key.
/// </summary>
public sealed class MediaCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"renotrack-media-{Guid.NewGuid():N}");

    public MediaCatalogTests()
    {
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(FixtureMediaRoot))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SiteOptions Site(bool social = true, string id = "testbild-startseite") => new()
    {
        Media = [new MediaItemOptions { Id = id, Alt = "Testbild (Testdaten)", SourceRef = "test-register-0001", HasSocialImage = social }],
        Home = new HomePageOptions { HeroImage = id },
    };

    private InvalidOperationException Refused(SiteOptions? site = null) =>
        Assert.Throws<InvalidOperationException>(() => MediaCatalog.Load(site ?? Site(), root));

    private string PathOf(string fileName) => Path.Combine(root, fileName);

    // ---- Loaded ------------------------------------------------------------------------------------

    [Fact]
    public void Every_derivative_of_a_listed_item_is_loaded_with_urls_srcsets_and_dimensions()
    {
        var catalog = MediaCatalog.Load(Site(), root);

        Assert.Equal((1, 7), (catalog.ImageCount, catalog.FileCount));
        var image = catalog.Image("testbild-startseite")!;
        Assert.Equal((1600, 1067), (image.Width, image.Height));
        Assert.Matches(
            "^/medien/testbild-startseite-480\\.webp\\?v=[0-9a-f]{16} 480w, /medien/testbild-startseite-960\\.webp\\?v=[0-9a-f]{16} 960w, /medien/testbild-startseite-1600\\.webp\\?v=[0-9a-f]{16} 1600w$",
            image.WebPSrcset);
        Assert.Matches(
            "^/medien/testbild-startseite-480\\.jpg\\?v=[0-9a-f]{16} 480w, /medien/testbild-startseite-960\\.jpg\\?v=[0-9a-f]{16} 960w, /medien/testbild-startseite-1600\\.jpg\\?v=[0-9a-f]{16} 1600w$",
            image.JpegSrcset);
        Assert.Matches("^/medien/testbild-startseite-960\\.jpg\\?v=[0-9a-f]{16}$", image.FallbackUrl);
        Assert.Equal("image/jpeg", image.Social!.ContentType);
    }

    [Fact]
    public void Without_a_social_image_none_is_loaded_or_required()
    {
        File.Delete(PathOf("testbild-startseite-og.jpg"));

        var catalog = MediaCatalog.Load(Site(social: false), root);

        Assert.Null(catalog.Image("testbild-startseite")!.Social);
        Assert.Null(catalog.File("testbild-startseite-og.jpg"));
    }

    /// <summary>S5-2: a file on disk that no listed item names is not in the catalog, so it can never be served.</summary>
    [Fact]
    public void Files_on_disk_that_no_item_lists_are_not_in_the_catalog()
    {
        var catalog = MediaCatalog.Load(Site(), root);

        Assert.Null(catalog.File("testbild-leistung-zwei-960.jpg"));
        Assert.Null(catalog.File("testbild-leistung-eins-og.jpg"));
    }

    [Theory]
    [InlineData("TESTBILD-STARTSEITE-960.JPG")]
    [InlineData("testbild-startseite-960.JPG")]
    [InlineData(" testbild-startseite-960.jpg")]
    [InlineData("../testbild-startseite-960.jpg")]
    [InlineData("testbild-startseite-960.jpg?v=1")]
    [InlineData(null)]
    public void A_file_is_found_only_by_its_exact_name(string? name)
    {
        Assert.Null(MediaCatalog.Load(Site(), root).File(name));
    }

    [Fact]
    public void No_listed_media_needs_no_media_directory()
    {
        Assert.Same(MediaCatalog.Empty, MediaCatalog.Load(new SiteOptions(), Path.Combine(root, "gibt-es-nicht")));
    }

    /// <summary>S5-12: an overwritten file gets a new URL, so a year of immutable caching can never serve it stale.</summary>
    [Fact]
    public void Changing_a_files_bytes_changes_its_version()
    {
        var before = MediaCatalog.Load(Site(), root).File("testbild-startseite-960.jpg")!.Version;
        File.Copy(PathOf("testbild-leistung-zwei-960.jpg"), PathOf("testbild-startseite-960.jpg"), overwrite: true);

        var after = MediaCatalog.Load(Site(), root).File("testbild-startseite-960.jpg")!.Version;

        Assert.NotEqual(before, after);
    }

    // ---- Refused -------------------------------------------------------------------------------------

    [Fact]
    public void A_missing_media_directory_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => MediaCatalog.Load(Site(), Path.Combine(root, "gibt-es-nicht")));

        Assert.Contains("'Site:Media'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("testbild-startseite-480.webp")]
    [InlineData("testbild-startseite-1600.jpg")]
    [InlineData("testbild-startseite-og.jpg")]
    public void A_missing_derivative_is_refused_naming_the_key_and_file(string fileName)
    {
        File.Delete(PathOf(fileName));

        var error = Refused();

        Assert.Contains("'Site:Media:0'", error.Message, StringComparison.Ordinal);
        Assert.Contains(fileName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_derivative_carrying_exif_is_refused()
    {
        File.WriteAllBytes(PathOf("testbild-startseite-480.jpg"), WithJpegSegment(Jpeg, 0xE1, ExifPayload()));

        var error = Refused();

        Assert.Contains("'Site:Media:0'", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ImageRefusal.Metadata), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_webp_carrying_xmp_is_refused()
    {
        File.WriteAllBytes(PathOf("testbild-startseite-480.webp"), ExtendedWebP(WebP, 0x04, ("XMP ", Ascii("<x:xmpmeta/>"))));

        Assert.Contains(nameof(ImageRefusal.Metadata), Refused().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_derivative_of_the_wrong_size_is_refused()
    {
        File.Copy(PathOf("testbild-startseite-480.jpg"), PathOf("testbild-startseite-960.jpg"), overwrite: true);

        Assert.Contains("480 × 320", Refused().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_webp_named_as_a_jpeg_is_refused()
    {
        File.Copy(PathOf("testbild-startseite-480.webp"), PathOf("testbild-startseite-480.jpg"), overwrite: true);

        Assert.Contains("WebP, not Jpeg", Refused().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_an_image_is_refused()
    {
        File.WriteAllText(PathOf("testbild-startseite-480.jpg"), "<html>nicht ein Bild</html>");

        Assert.Contains(nameof(ImageRefusal.UnrecognisedFormat), Refused().Message, StringComparison.Ordinal);
    }

    /// <summary>S5-11: the budget is checked on the real byte length, before the file is read at all.</summary>
    [Theory]
    [InlineData("testbild-startseite-480.jpg", MediaDerivatives.Small)]
    [InlineData("testbild-startseite-960.webp", MediaDerivatives.Medium)]
    [InlineData("testbild-startseite-1600.jpg", MediaDerivatives.Large)]
    [InlineData("testbild-startseite-og.jpg", MediaDerivatives.SocialBudget)]
    public void A_derivative_one_byte_over_its_budget_is_refused(string fileName, long budget)
    {
        File.WriteAllBytes(PathOf(fileName), new byte[budget + 1]);

        var error = Refused();

        Assert.Contains($"{budget + 1} bytes", error.Message, StringComparison.Ordinal);
        Assert.Contains($"budget of {budget}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_budgets_are_the_provisional_values_until_real_media_validation()
    {
        Assert.Equal(350 * 1024, MediaDerivatives.Large);
        Assert.Equal(160 * 1024, MediaDerivatives.Medium);
        Assert.Equal(60 * 1024, MediaDerivatives.Small);
        Assert.Equal(250 * 1024, MediaDerivatives.SocialBudget);
        Assert.All(MediaDerivatives.Content, derivative => Assert.Equal(derivative.Width * 2 / 3.0, derivative.Height, 0.5));
    }
}
