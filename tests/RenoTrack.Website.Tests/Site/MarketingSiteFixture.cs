using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// The real application with the marketing site enabled, booted once per test class against a copy of
/// the fictional Alpha content pack (<b>D102</b>, <b>D103</b>).
/// </summary>
public sealed class MarketingSiteFixture : IDisposable
{
    internal const string CanonicalOrigin = "https://www.alpha-testbetrieb.test";
    internal const string AliasOrigin = "https://alpha-testbetrieb.test";
    internal const string CompanyName = "Alpha Testbetrieb (Testdaten)";

    private readonly TemporaryContentPack pack = TemporaryContentPack.CopyOf(TemporaryContentPack.Alpha);

    public MarketingSiteFixture()
    {
        Factory = new ContentPackFactory(pack.Root);
    }

    public ContentPackFactory Factory { get; }

    /// <summary>A client that reports redirects instead of following them, on the canonical host.</summary>
    public HttpClient Client(string origin = CanonicalOrigin) => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri(origin),
    });

    public void Dispose()
    {
        Factory.Dispose();
        pack.Dispose();
    }
}

/// <summary>Small HTML assertions the site tests share.</summary>
internal static partial class Html
{
    internal static int Count(string html, string needle) =>
        Regex.Matches(html, Regex.Escape(needle), RegexOptions.CultureInvariant).Count;

    /// <summary>The value of the first <c>content</c> or <c>href</c> attribute on a tag containing <paramref name="marker"/>.</summary>
    internal static string? AttributeOf(string html, string marker, string attribute)
    {
        var tag = TagPattern().Matches(html).Select(match => match.Value).FirstOrDefault(value => value.Contains(marker, StringComparison.Ordinal));
        if (tag is null)
        {
            return null;
        }

        var value = Regex.Match(tag, $"{attribute}=\"(?<value>[^\"]*)\"");
        return value.Success ? value.Groups["value"].Value : null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
