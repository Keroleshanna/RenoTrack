using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>What the service-page test classes share (<b>D105</b>). Fictional packs only (<b>D100</b>).</summary>
internal static partial class ServicePageFixtures
{
    internal const string AlphaOverviewTitle = "Alle Testleistungen in Testort Alpha (Testdaten)";
    internal const string AlphaOverviewHeadline = "Erfundene Testleistungen im Überblick (Testdaten)";
    internal const string AlphaOverviewIntro = "Eine erfundene Einleitung für die Leistungsübersicht (Testdaten).";

    internal const string EinsPath = "/leistungen/test-leistung-eins";
    internal const string ZweiPath = "/leistungen/test-leistung-zwei";
    internal const string EinsTitle = "Testleistung Eins in Testort Alpha (Testdaten)";
    internal const string EinsHeadline = "Erfundene Testleistung Eins für Testort Alpha (Testdaten)";
    internal const string EinsDescription = "Eine erfundene Beschreibung der Testleistung Eins (Testdaten).";
    internal const string ZweiTitle = "Testleistung Zwei in Testort Alpha (Testdaten)";

    /// <summary>A complete, fictional enabled site whose <c>Site:Services</c> and <c>Site:ServicesPage</c> the test supplies.</summary>
    internal static string Pack(string servicesSection) => $$"""
        {
          "CompanyIdentity": {
            "DisplayName": "Testfirma (Testdaten)",
            "ContactPhone": "+49 000 4444444",
            "ContactEmail": "kontakt@example.test",
            "Address": { "StreetAddress": "Teststraße 4", "PostalCode": "00004", "Locality": "Testort", "CountryCode": "DE" }
          },
          "Site": {
            "PublicBaseUrl": "https://www.example.test",
            "Home": { "MetaTitle": "Startseitentitel (Testdaten)" },
            {{servicesSection}}
          }
        }
        """;

    /// <summary>Boots a pack the test writes and answers one GET, without following redirects.</summary>
    internal static async Task<(System.Net.HttpStatusCode Status, string Html)> Get(string siteJson, string path)
    {
        using var pack = TemporaryContentPack.With(siteJson);
        using var factory = new ContentPackFactory(pack.Root);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://www.example.test"),
        });

        using var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A client on the Beta pack, which supplies only the required keys.</summary>
    internal static (TemporaryContentPack Pack, ContentPackFactory Factory, HttpClient Client) Beta()
    {
        var pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Beta);
        var factory = new ContentPackFactory(pack.Root);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://beta-musterwerkstatt.test"),
        });
        return (pack, factory, client);
    }

    /// <summary>From the element labelled by <paramref name="id"/> to the end of its section.</summary>
    internal static string Section(string html, string id)
    {
        var start = html.IndexOf($"aria-labelledby=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"section '{id}' is missing");
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>The breadcrumb navigation's markup.</summary>
    internal static string Breadcrumb(string html)
    {
        var start = html.IndexOf("<nav class=\"page-breadcrumb\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "breadcrumb is missing");
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
    }

    /// <summary>The page's main content, without header and footer, so shared chrome does not satisfy an assertion.</summary>
    internal static string MainContent(string html)
    {
        var start = html.IndexOf("<main", StringComparison.Ordinal);
        Assert.True(start >= 0, "main is missing");
        return html[start..html.IndexOf("</main>", start, StringComparison.Ordinal)];
    }

    internal static IReadOnlyList<string> SameOriginLinks(string html) =>
        HrefPattern().Matches(html)
            .Select(match => match.Groups["href"].Value)
            .Where(href => href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    internal static IReadOnlyList<string> OffOriginUrls(string html, string canonicalOrigin) =>
        UrlAttributePattern().Matches(html)
            .Select(match => match.Groups["url"].Value)
            .Where(url => url.Contains("//", StringComparison.Ordinal) && !url.StartsWith(canonicalOrigin + "/", StringComparison.Ordinal))
            .ToList();

    [GeneratedRegex("href=\"(?<href>[^\"]*)\"")]
    private static partial Regex HrefPattern();

    [GeneratedRegex("(?:href|src|content)=\"(?<url>[^\"]*)\"")]
    private static partial Regex UrlAttributePattern();
}
