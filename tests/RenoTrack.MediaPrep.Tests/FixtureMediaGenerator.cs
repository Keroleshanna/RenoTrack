namespace RenoTrack.MediaPrep.Tests;

/// <summary>
/// Regenerates the Website tests' fixture media with the real pipeline (<b>D106</b>, S5-13). Synthetic test graphics
/// only; no photograph is ever committed.
/// </summary>
/// <remarks>
/// Writes only when <c>RENOTRACK_WRITE_FIXTURE_MEDIA</c> names the target directory — normally
/// <c>tests/RenoTrack.Website.Tests/Fixtures/ContentPacks/Alpha/media</c>, emptied first. Otherwise it proves the
/// generation succeeds into a temporary directory. The committed files are verified by the Website's own tests, not
/// compared here byte for byte: the encoder's output is reproducible for one platform, and CI builds on another.
/// </remarks>
public sealed class FixtureMediaGenerator
{
    internal const string TargetVariable = "RENOTRACK_WRITE_FIXTURE_MEDIA";

    /// <summary>The fixture items: two referenced by the Alpha pack, one on disk but deliberately unlisted.</summary>
    internal static readonly (string Id, int Palette, bool Social)[] Items =
    [
        ("testbild-startseite", 0, true),
        ("testbild-leistung-eins", 1, true),
        ("testbild-leistung-zwei", 2, false),
    ];

    [Fact]
    public void The_fixture_media_are_generated_by_the_real_pipeline()
    {
        var target = Environment.GetEnvironmentVariable(TargetVariable);
        var scratch = Path.Combine(Path.GetTempPath(), $"renotrack-fixture-media-{Guid.NewGuid():N}");
        var source = Path.Combine(scratch, "source");
        var output = string.IsNullOrWhiteSpace(target) ? Path.Combine(scratch, "out") : target;
        Directory.CreateDirectory(source);

        try
        {
            foreach (var (id, palette, social) in Items)
            {
                using var bitmap = SyntheticImages.Pattern(2400, 1600, palette);
                File.WriteAllBytes(Path.Combine(source, $"{id}.png"), SyntheticImages.EncodePng(bitmap));

                var prepared = MediaPipeline.Prepare(
                    new CropSpecItem(id, $"{id}.png", new CropRect(0, 0, 2400, 1600), social ? new CropRect(0, 170, 2400, 1260) : null),
                    source,
                    output);

                Assert.All(prepared, file => Assert.True(file.IsWithinBudget, $"{file.FileName} is {file.Bytes} bytes"));
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
