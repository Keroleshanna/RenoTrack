using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// <b>The content pack cannot influence product or security configuration</b> (<b>D102</b>, S1-8) —
/// proven on the provider itself and on a running host.
/// </summary>
/// <remarks>
/// <para>
/// The stakes are concrete. The pack sits above <c>appsettings.json</c>, so a <c>Logging</c> section
/// could re-enable the logging that <b>D101</b> and <c>CLAUDE.md</c> §24 keep off token routes, and a
/// <c>PublicApi</c> or <c>TrustedForwarders</c> section could redirect a customer's token traffic or
/// widen the forwarder trust list. If <see cref="IsolatedContentPackProvider"/> is ever replaced with
/// <c>AddJsonFile</c>, these tests are what fail.
/// </para>
/// </remarks>
public sealed class ContentPackIsolationTests
{
    private const string SecretSentinel = "Password=do-not-echo-7c21";

    private const string ValidIdentity = """
        "CompanyIdentity": { "DisplayName": "Testfirma (Testdaten)" }
        """;

    private static IsolatedContentPackProvider Provider(TemporaryContentPack pack, string fileName, bool optional) =>
        (IsolatedContentPackProvider)new ContentPackConfigurationSource(pack.Root, fileName, optional)
            .Build(new ConfigurationBuilder());

    private static IEnumerable<string> AllKeys(IConfigurationProvider provider, string? parent = null)
    {
        foreach (var child in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = parent is null ? child : $"{parent}:{child}";
            if (provider.TryGet(key, out _))
            {
                yield return key;
            }

            foreach (var descendant in AllKeys(provider, key))
            {
                yield return descendant;
            }
        }
    }

    // ---- The provider refuses before anything is exposed ---------------------

    [Theory]
    [InlineData("ConnectionStrings", """{ "RenoTrackDb": "Server=db;Password=do-not-echo-7c21" }""")]
    [InlineData("Jwt", """{ "SigningKey": "Password=do-not-echo-7c21" }""")]
    [InlineData("Email", """{ "Password": "Password=do-not-echo-7c21" }""")]
    [InlineData("TokenLink", """{ "PublicBaseUrl": "https://attacker.example.test" }""")]
    [InlineData("Logging", """{ "LogLevel": { "Default": "Trace", "Microsoft.AspNetCore": "Trace" } }""")]
    [InlineData("PublicApi", """{ "BaseUrl": "https://attacker.example.test" }""")]
    [InlineData("TrustedForwarders", """{ "KnownNetworks": [ "0.0.0.0/0" ] }""")]
    [InlineData("ContentPack", """{ "RootPath": "C:\\elsewhere" }""")]
    public void A_manifest_carrying_product_configuration_is_refused_without_echoing_its_values(string section, string body)
    {
        using var pack = TemporaryContentPack.With($$"""{ {{ValidIdentity}}, "{{section}}": {{body}} }""");
        var provider = Provider(pack, ContentPackSectionPolicy.SiteFileName, optional: false);

        var error = Assert.Throws<InvalidOperationException>(provider.Load);

        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker", error.Message, StringComparison.Ordinal);
        Assert.Empty(AllKeys(provider));
    }

    [Theory]
    [InlineData("""{ "Legal": { "Impressum": { "Sections": [ { "Heading": "H", "Paragraphs": [ { "Text": "T" } ] } ] } }, "CompanyIdentity": { "DisplayName": "X" } }""", "CompanyIdentity")]
    [InlineData("""{ "Legal": { "Impressum": { "Sections": [ { "Heading": "H", "Paragraphs": [ { "Text": "T" } ] } ] } }, "Logging": { "LogLevel": { "Default": "Trace" } } }""", "Logging")]
    public void The_legal_file_is_refused_any_section_but_legal(string legalJson, string section)
    {
        using var pack = TemporaryContentPack.With("""{ "CompanyIdentity": { "DisplayName": "X" } }""", legalJson);
        var provider = Provider(pack, ContentPackSectionPolicy.LegalFileName, optional: true);

        var error = Assert.Throws<InvalidOperationException>(provider.Load);

        Assert.Contains("'legal.json'", error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colon_escaped_property_name_is_refused_by_the_provider()
    {
        using var pack = TemporaryContentPack.With($$"""{ {{ValidIdentity}}, "Logging:LogLevel:Default": "Trace" }""");

        var error = Assert.Throws<InvalidOperationException>(Provider(pack, ContentPackSectionPolicy.SiteFileName, false).Load);

        Assert.Contains("'Logging'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_comment_key_is_refused_because_it_is_a_section_of_its_own()
    {
        using var pack = TemporaryContentPack.With($$"""{ "//": "Kommentar", {{ValidIdentity}} }""");

        var error = Assert.Throws<InvalidOperationException>(Provider(pack, ContentPackSectionPolicy.SiteFileName, false).Load);

        Assert.Contains("'//'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "Site": "https://www.example.test" }""")]
    [InlineData("""{ "Site": {} }""")]
    [InlineData("""{ "CompanyIdentity": null }""")]
    [InlineData("{}")]
    public void A_manifest_without_a_section_with_content_is_refused(string siteJson)
    {
        using var pack = TemporaryContentPack.With(siteJson);

        Assert.Throws<InvalidOperationException>(Provider(pack, ContentPackSectionPolicy.SiteFileName, false).Load);
    }

    /// <summary>Absent is a meaning; present-but-empty is a mistake.</summary>
    [Fact]
    public void An_absent_optional_legal_file_contributes_nothing_but_an_empty_one_is_refused()
    {
        using var pack = TemporaryContentPack.With("""{ "CompanyIdentity": { "DisplayName": "X" } }""");

        var absent = Provider(pack, ContentPackSectionPolicy.LegalFileName, optional: true);
        absent.Load();
        Assert.Empty(AllKeys(absent));

        pack.Write(ContentPackSectionPolicy.LegalFileName, "{}");
        Assert.Throws<InvalidOperationException>(Provider(pack, ContentPackSectionPolicy.LegalFileName, true).Load);
    }

    [Theory]
    [InlineData("""{ "CompanyIdentity": { "DisplayName": "Password=do-not-echo-7c21" """)]
    [InlineData("""[ "Password=do-not-echo-7c21" ]""")]
    [InlineData("""{ "CompanyIdentity": { "DisplayName": "A", "displayname": "Password=do-not-echo-7c21" } }""")]
    public void Unreadable_json_is_refused_naming_the_file_but_not_its_content(string siteJson)
    {
        using var pack = TemporaryContentPack.With(siteJson);

        var error = Assert.Throws<InvalidOperationException>(Provider(pack, ContentPackSectionPolicy.SiteFileName, false).Load);

        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretSentinel, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_file_exposes_exactly_its_allowed_sections()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        var provider = Provider(pack, ContentPackSectionPolicy.SiteFileName, optional: false);

        provider.Load();
        var keys = AllKeys(provider).ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(
            key.StartsWith("CompanyIdentity:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Site:", StringComparison.OrdinalIgnoreCase),
            $"Unexpected key '{key}'."));
    }

    // ---- On a running host ---------------------------------------------------

    [Theory]
    [InlineData("Logging", """{ "LogLevel": { "Microsoft.AspNetCore": "Information", "System.Net.Http.HttpClient": "Trace" } }""")]
    [InlineData("PublicApi", """{ "BaseUrl": "https://attacker.example.test" }""")]
    [InlineData("TrustedForwarders", """{ "KnownNetworks": [ "0.0.0.0/0" ] }""")]
    public void The_application_refuses_to_start_on_a_pack_that_reaches_for_product_configuration(string section, string body)
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        var site = File.ReadAllText(Path.Combine(pack.Root, ContentPackSectionPolicy.SiteFileName)).TrimEnd();
        pack.Write(ContentPackSectionPolicy.SiteFileName, $"{site[..^1]}, \"{section}\": {body} }}");
        using var factory = new ContentPackFactory(pack.Root);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"'{section}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'site.json'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// With a real pack loaded, every key a pack provider exposes is content, and the load-bearing
    /// product settings keep the values the application itself configures.
    /// </summary>
    [Fact]
    public void A_running_host_exposes_only_content_from_the_pack_and_keeps_its_own_product_settings()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root);
        _ = factory.CreateClient();

        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        var packProviders = configuration.Providers.OfType<IsolatedContentPackProvider>().ToList();

        Assert.Equal(2, packProviders.Count);
        Assert.All(packProviders.SelectMany(provider => AllKeys(provider)), key => Assert.True(
            key.StartsWith("CompanyIdentity:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Site:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Legal:", StringComparison.OrdinalIgnoreCase),
            $"Pack exposed non-content key '{key}'."));

        Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft.AspNetCore"]);
        Assert.Equal("Warning", configuration["Logging:LogLevel:System.Net.Http.HttpClient"]);
        Assert.Equal("https://api.example.test", configuration["PublicApi:BaseUrl"]);
    }

    /// <summary>The agreed precedence, observed on the real host's own provider list.</summary>
    [Fact]
    public void On_a_running_host_the_pack_sits_above_every_file_source_and_below_the_environment()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root);
        _ = factory.CreateClient();

        var providers = ((IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>()).Providers.ToList();
        var firstPack = providers.FindIndex(provider => provider is IsolatedContentPackProvider);
        var lastPack = providers.FindLastIndex(provider => provider is IsolatedContentPackProvider);
        var lastFile = providers.FindLastIndex(provider => provider is FileConfigurationProvider);
        var lastEnvironment = providers.FindLastIndex(provider => provider is EnvironmentVariablesConfigurationProvider);

        Assert.True(lastFile < firstPack, "A file source (appsettings/user-secrets) outranks the pack.");
        Assert.True(lastPack < lastEnvironment, "The pack outranks the application's environment variables.");
    }

    /// <summary>
    /// An operator's scalar override stays possible, and wins — which also proves the test host's own
    /// settings source sits above the pack, so every other test here observes pack values correctly.
    /// </summary>
    [Fact]
    public void A_scalar_override_outside_the_pack_is_allowed_and_wins()
    {
        using var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);
        using var factory = new ContentPackFactory(pack.Root, ("CompanyIdentity:ContactPhone", "+49 000 9999999"));
        _ = factory.CreateClient();

        Assert.Equal("+49 000 9999999", factory.Services.GetRequiredService<CompanyIdentityOptions>().ContactPhone);
    }
}
