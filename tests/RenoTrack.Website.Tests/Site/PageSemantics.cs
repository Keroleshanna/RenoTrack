using System.Text.RegularExpressions;

namespace RenoTrack.Website.Tests.Site;

/// <summary>
/// Structural accessibility checks over a rendered marketing page (<b>D105</b>): the properties that reusing a
/// partial on several pages could silently break.
/// </summary>
/// <remarks>
/// Deliberately small and regex-based over this project's own server-rendered markup, which it can read reliably
/// because every page is written here. It is not a general HTML validator.
/// </remarks>
internal static partial class PageSemantics
{
    /// <summary>Asserts every rule, each failure naming the page and what was found.</summary>
    internal static void AssertWellFormed(string html, string page)
    {
        AssertIdsAreUnique(html, page);
        AssertLabelReferencesResolve(html, page);
        AssertHeadingOutline(html, page);
        AssertLandmarks(html, page);
    }

    internal static IReadOnlyList<int> HeadingLevels(string html) =>
        HeadingPattern().Matches(html).Select(match => int.Parse(match.Groups["level"].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    private static void AssertIdsAreUnique(string html, string page)
    {
        var duplicates = IdPattern().Matches(html)
            .Select(match => match.Groups["id"].Value)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"{page}: duplicate id(s) {string.Join(", ", duplicates)}");
    }

    private static void AssertLabelReferencesResolve(string html, string page)
    {
        var ids = IdPattern().Matches(html).Select(match => match.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

        var dangling = LabelledByPattern().Matches(html)
            .SelectMany(match => match.Groups["ids"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(id => !ids.Contains(id))
            .ToList();

        Assert.True(dangling.Count == 0, $"{page}: aria-labelledby references missing id(s) {string.Join(", ", dangling)}");
    }

    /// <summary>One h1, first; and no heading more than one level deeper than the heading before it.</summary>
    private static void AssertHeadingOutline(string html, string page)
    {
        var levels = HeadingLevels(html);

        Assert.True(levels.Count > 0, $"{page}: no headings");
        Assert.True(levels.Count(level => level == 1) == 1, $"{page}: expected exactly one h1, outline {string.Join(" ", levels)}");
        Assert.True(levels[0] == 1, $"{page}: the first heading is h{levels[0]}, not h1");

        for (var index = 1; index < levels.Count; index++)
        {
            Assert.True(
                levels[index] <= levels[index - 1] + 1,
                $"{page}: h{levels[index - 1]} is followed by h{levels[index]}, skipping a level; outline {string.Join(" ", levels)}");
        }
    }

    /// <summary>One banner, one main, one contentinfo; every nav named, and no two navs with the same name.</summary>
    private static void AssertLandmarks(string html, string page)
    {
        Assert.Equal(1, Html.Count(html, "<header"));
        Assert.Equal(1, Html.Count(html, "<main"));
        Assert.Equal(1, Html.Count(html, "<footer"));

        var navs = NavPattern().Matches(html).Select(match => match.Value).ToList();
        var names = navs.Select(nav =>
        {
            var label = Regex.Match(nav, "aria-label=\"(?<name>[^\"]+)\"");
            var labelledBy = Regex.Match(nav, "aria-labelledby=\"(?<name>[^\"]+)\"");
            Assert.True(label.Success || labelledBy.Success, $"{page}: unnamed navigation landmark {nav}");
            return label.Success ? "label:" + label.Groups["name"].Value : "id:" + labelledBy.Groups["name"].Value;
        }).ToList();

        Assert.True(names.Distinct(StringComparer.Ordinal).Count() == names.Count, $"{page}: navigation landmarks share a name: {string.Join(", ", names)}");
    }

    [GeneratedRegex("\\sid=\"(?<id>[^\"]+)\"")]
    private static partial Regex IdPattern();

    [GeneratedRegex("aria-labelledby=\"(?<ids>[^\"]+)\"")]
    private static partial Regex LabelledByPattern();

    [GeneratedRegex("<h(?<level>[1-6])[\\s>]")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("<nav\\b[^>]*>")]
    private static partial Regex NavPattern();
}
