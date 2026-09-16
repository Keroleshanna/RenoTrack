using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// The real application, booted against a content pack in a temporary directory (<b>D102</b>). No
/// database and no API: the Website test project stays runnable on any OS (<c>CLAUDE.md</c> §24).
/// </summary>
public sealed partial class ContentPackStartupTests
{
    private const string Token = "9RfB-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private const string AlphaName = "Alpha Testbetrieb (Testdaten)";
    private const string BetaName = "Beta Musterwerkstatt (Testdaten)";

    // ---- A pack loads --------------------------------------------------------

    [Fact]
    public void The_pack_binds_identity_and_site_content()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root);
        _ = factory.CreateClient();

        var identity = factory.Services.GetRequiredService<CompanyIdentityOptions>();
        var site = factory.Services.GetRequiredService<SiteOptions>();

        Assert.Equal(AlphaName, identity.DisplayName);
        Assert.Equal("+49 000 1111111", identity.ContactPhone);
        Assert.Equal("Testort Alpha", identity.Address.Locality);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            identity.OpeningHours.Single().DaysOfWeek);
        Assert.Equal(2, identity.ServiceArea.Places.Count);

        Assert.True(site.IsEnabled);
        Assert.Equal("https://www.alpha-testbetrieb.test", site.CanonicalOrigin);
        Assert.Equal(new[] { "test-leistung-eins", "test-leistung-zwei" }, site.Services.Select(service => service.Slug));
    }

    [Fact]
    public void Without_a_pack_the_site_is_disabled_and_the_application_starts_as_before()
    {
        using var factory = new ContentPackFactory(packRoot: null);
        _ = factory.CreateClient();

        Assert.False(factory.Services.GetRequiredService<SiteOptions>().IsEnabled);
    }

    // ---- A pack that cannot be used fails startup, naming why ----------------

    [Fact]
    public void A_relative_root_path_fails_startup()
    {
        using var factory = new ContentPackFactory("content-pack");

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'ContentPack:RootPath'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_directory_fails_startup()
    {
        using var factory = new ContentPackFactory(Path.Combine(Path.GetTempPath(), $"renotrack-missing-{Guid.NewGuid():N}"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'ContentPack:RootPath'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pack_without_its_manifest_fails_startup()
    {
        using var pack = TemporaryContentPack.With(siteJson: null);
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"CompanyIdentity\": ")]
    [InlineData("{}")]
    public void A_malformed_or_empty_manifest_fails_startup_naming_the_file(string siteJson)
    {
        using var pack = TemporaryContentPack.With(siteJson);
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enabled_site_with_an_incomplete_identity_fails_startup_naming_every_missing_key()
    {
        using var pack = TemporaryContentPack.With("""
            {
              "CompanyIdentity": { "DisplayName": "Testfirma (Testdaten)" },
              "Site": { "PublicBaseUrl": "https://www.example.test" }
            }
            """);
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("'CompanyIdentity:ContactPhone'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:ContactEmail'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'CompanyIdentity:Address'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Site:Services'", error.Message, StringComparison.Ordinal);
    }

    // ---- Lists come from one source ------------------------------------------

    [Theory]
    [InlineData("Site:Services:0:Name", "Site:Services")]
    [InlineData("CompanyIdentity:OpeningHours:0:Opens", "CompanyIdentity:OpeningHours")]
    [InlineData("CompanyIdentity:ServiceArea:Places:0:Name", "CompanyIdentity:ServiceArea:Places")]
    public void A_list_supplied_by_the_pack_and_another_source_fails_startup(string key, string section)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root, (key, "Überschrieben"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Content pack 'site.json'", error.Message, StringComparison.Ordinal);
    }

    // ---- Legal content from the pack ----------------------------------------

    [Fact]
    public async Task Legal_content_in_the_pack_is_served_and_absent_legal_content_is_a_404()
    {
        using var alpha = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var beta = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var withLegal = new ContentPackFactory(alpha.Root);
        using var withoutLegal = new ContentPackFactory(beta.Root);

        using var served = await withLegal.CreateClient().GetAsync("/impressum");
        using var missing = await withoutLegal.CreateClient().GetAsync("/impressum");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("Erfundener Testtext", await served.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // ---- The brand mount moves into the pack; nothing else is served ----------

    [Fact]
    public async Task The_logo_is_served_from_the_packs_brand_directory()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root);

        using var response = await factory.CreateClient().GetAsync("/brand/logo.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/site.json")]
    [InlineData("/legal.json")]
    [InlineData("/brand/..%2fsite.json")]
    [InlineData("/brand/%2e%2e/site.json")]
    [InlineData("/brand/%2e%2e%2flegal.json")]
    [InlineData("/brand/..%5csite.json")]
    [InlineData("/brand/")]
    public async Task Pack_files_outside_the_brand_directory_are_never_served(string path)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root);

        using var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Testleistung", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicBaseUrl", body, StringComparison.Ordinal);
    }

    // ---- Another company, same build -----------------------------------------

    /// <summary>
    /// <b>The multi-company requirement, as a test.</b> The same build boots two unrelated fictional
    /// companies, and neither company's content appears in the other's bound options or rendered page.
    /// </summary>
    [Fact]
    public async Task Two_packs_run_on_the_same_build_without_either_leaking_into_the_other()
    {
        using var alphaPack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var betaPack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        using var alpha = new ContentPackFactory(alphaPack.Root);
        using var beta = new ContentPackFactory(betaPack.Root);

        var alphaHtml = await alpha.CreateClient().GetStringAsync($"/angebot/{Token}");
        var betaHtml = await beta.CreateClient().GetStringAsync($"/angebot/{Token}");

        Assert.Contains(AlphaName, alphaHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(BetaName, alphaHtml, StringComparison.Ordinal);
        Assert.Contains(BetaName, betaHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(AlphaName, betaHtml, StringComparison.Ordinal);

        var alphaSite = alpha.Services.GetRequiredService<SiteOptions>();
        var betaSite = beta.Services.GetRequiredService<SiteOptions>();
        Assert.Equal("https://beta-musterwerkstatt.test", betaSite.CanonicalOrigin);
        Assert.Equal(new[] { "beta-testleistung" }, betaSite.Services.Select(service => service.Slug));
        Assert.DoesNotContain(alphaSite.Services, service => service.Slug == "beta-testleistung");
        Assert.Null(beta.Services.GetRequiredService<CompanyIdentityOptions>().OwnerName);
    }

    // ---- Fixtures stay fictional ---------------------------------------------

    /// <summary>
    /// <b>No real company data in this repository</b> (<b>D100</b>): every email address and web host in
    /// the fixture packs uses the reserved <c>.test</c> domain, and every phone number the unassigned
    /// <c>+49 000</c> range.
    /// </summary>
    [Fact]
    public void Fixture_packs_use_only_reserved_domains_and_unassigned_numbers()
    {
        var files = Directory.EnumerateFiles(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContentPacks"), "*.json", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);

        foreach (var text in files.Select(File.ReadAllText))
        {
            foreach (Match host in HostPattern().Matches(text))
            {
                Assert.EndsWith(".test", host.Groups["host"].Value, StringComparison.Ordinal);
            }

            foreach (Match phone in PhonePattern().Matches(text))
            {
                Assert.StartsWith("+49 000 ", phone.Value, StringComparison.Ordinal);
            }
        }
    }

    [GeneratedRegex(@"(?:@|https?://)(?<host>[A-Za-z0-9.-]+)")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"\+[0-9][0-9 ]{6,}")]
    private static partial Regex PhonePattern();
}
