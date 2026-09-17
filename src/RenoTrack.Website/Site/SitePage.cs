using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RenoTrack.Website.Site;

/// <summary>
/// The page-level facts the marketing layout renders into <c>&lt;head&gt;</c>: an optional description, and
/// whether the page may be indexed (<b>D103</b>). The title stays in <c>ViewData["Title"]</c>, where every
/// existing page already puts it.
/// </summary>
public static class SitePage
{
    private const string DescriptionKey = "SitePage.Description";
    private const string NoIndexKey = "SitePage.NoIndex";

    public static void SetDescription(this ViewDataDictionary viewData, string description) =>
        viewData[DescriptionKey] = description;

    public static string? Description(this ViewDataDictionary viewData) => viewData[DescriptionKey] as string;

    /// <summary>Marks the page as not to be indexed — e.g. the not-found page.</summary>
    public static void SetNoIndex(this ViewDataDictionary viewData) => viewData[NoIndexKey] = true;

    public static bool IsIndexable(this ViewDataDictionary viewData) => viewData[NoIndexKey] is not true;
}
