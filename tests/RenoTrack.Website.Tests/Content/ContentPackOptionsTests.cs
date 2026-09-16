using Microsoft.Extensions.Configuration;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// Locating the pack, and where its files sit among the configuration sources (<b>D102</b>, S1-4 as
/// amended by S1-8).
/// </summary>
public sealed class ContentPackOptionsTests
{
    private const string Key = "'ContentPack:RootPath'";

    // ---- Locating the pack ---------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_root_path_means_no_pack_and_nothing_to_validate(string? rootPath)
    {
        var options = new ContentPackOptions { RootPath = rootPath };

        options.Validate();

        Assert.False(options.IsConfigured);
    }

    [Theory]
    [InlineData("content-pack")]
    [InlineData("./content-pack")]
    [InlineData("../content-pack")]
    public void A_relative_path_is_refused(string rootPath)
    {
        var error = Assert.Throws<InvalidOperationException>(new ContentPackOptions { RootPath = rootPath }.Validate);

        Assert.Contains(Key, error.Message, StringComparison.Ordinal);
        Assert.Contains("not an absolute path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_directory_is_refused()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"renotrack-missing-{Guid.NewGuid():N}");

        var error = Assert.Throws<InvalidOperationException>(new ContentPackOptions { RootPath = missing }.Validate);

        Assert.Contains(Key, error.Message, StringComparison.Ordinal);
        Assert.Contains("not an existing directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_without_a_manifest_is_refused()
    {
        using var pack = TemporaryContentPack.With(siteJson: null, legalJson: """{ "Legal": { "Impressum": { "Sections": [] } } }""");

        var error = Assert.Throws<InvalidOperationException>(new ContentPackOptions { RootPath = pack.Root }.Validate);

        Assert.Contains(Key, error.Message, StringComparison.Ordinal);
        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_brand_mount_moves_into_the_pack_only_when_one_is_configured()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        var contentRoot = Path.Combine(Path.GetTempPath(), "app");

        Assert.Equal(Path.Combine(contentRoot, "brand"), new ContentPackOptions().BrandRootFor(contentRoot));
        Assert.Equal(
            Path.Combine(Path.TrimEndingDirectorySeparator(pack.Root), "brand"),
            new ContentPackOptions { RootPath = pack.Root + Path.DirectorySeparatorChar }.BrandRootFor(contentRoot));
    }

    // ---- Precedence ----------------------------------------------------------

    /// <summary>
    /// Built from the same source kinds, in the same order, <c>WebApplication.CreateBuilder</c> uses:
    /// a prefixed host environment source first, then the JSON files, then the unprefixed environment
    /// source and the command line. The prefixed host source is what makes "insert before the first
    /// environment source" wrong — it sits below the JSON files.
    /// </summary>
    [Fact]
    public void Content_sits_above_appsettings_and_below_environment_variables_and_the_command_line()
    {
        using var pack = TemporaryContentPack.With(siteJson: """
            {
              "CompanyIdentity": {
                "DisplayName": "Aus dem Pack",
                "OwnerName": "Aus dem Pack",
                "OpeningHoursNote": "Aus dem Pack"
              }
            }
            """);
        pack.Write("appsettings.json", """
            {
              "CompanyIdentity": {
                "DisplayName": "Aus appsettings",
                "OwnerName": "Aus appsettings",
                "OpeningHoursNote": "Aus appsettings",
                "ContactEmail": "nur-appsettings@example.test"
              }
            }
            """);

        var prefix = $"RENOTRACK_TEST_{Guid.NewGuid():N}_";
        var hostPrefix = $"RENOTRACK_HOST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable($"{prefix}CompanyIdentity__OwnerName", "Aus der Umgebung");
        try
        {
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables(hostPrefix)
                .AddJsonFile(Path.Combine(pack.Root, "appsettings.json"))
                .AddEnvironmentVariables(prefix)
                .AddCommandLine(["--CompanyIdentity:OpeningHoursNote=Von der Befehlszeile"]);

            new ContentPackOptions { RootPath = pack.Root }.AddTo(builder.Sources);
            var configuration = builder.Build();

            Assert.Equal("Aus dem Pack", configuration["CompanyIdentity:DisplayName"]);
            Assert.Equal("Aus der Umgebung", configuration["CompanyIdentity:OwnerName"]);
            Assert.Equal("Von der Befehlszeile", configuration["CompanyIdentity:OpeningHoursNote"]);
            Assert.Equal("nur-appsettings@example.test", configuration["CompanyIdentity:ContactEmail"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}CompanyIdentity__OwnerName", null);
        }
    }

    [Fact]
    public void With_no_file_source_the_pack_still_sits_below_environment_and_command_line()
    {
        using var pack = TemporaryContentPack.With(siteJson: """{ "CompanyIdentity": { "DisplayName": "Aus dem Pack" } }""");

        var builder = new ConfigurationBuilder()
            .AddCommandLine(["--CompanyIdentity:DisplayName=Von der Befehlszeile"]);

        new ContentPackOptions { RootPath = pack.Root }.AddTo(builder.Sources);

        Assert.Equal("Von der Befehlszeile", builder.Build()["CompanyIdentity:DisplayName"]);
    }

    [Fact]
    public void Adding_an_unconfigured_pack_changes_nothing()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection();
        var before = builder.Sources.Count;

        new ContentPackOptions().AddTo(builder.Sources);

        Assert.Equal(before, builder.Sources.Count);
    }
}
