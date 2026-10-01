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
    private const string DocumentTitleKey = "SitePage.DocumentTitle";
    private const string FullWidthKey = "SitePage.FullWidth";

    public static void SetDescription(this ViewDataDictionary viewData, string description) =>
        viewData[DescriptionKey] = description;

    public static string? Description(this ViewDataDictionary viewData) => viewData[DescriptionKey] as string;

    /// <summary>Marks the page as not to be indexed — e.g. the not-found page.</summary>
    public static void SetNoIndex(this ViewDataDictionary viewData) => viewData[NoIndexKey] = true;

    public static bool IsIndexable(this ViewDataDictionary viewData) => viewData[NoIndexKey] is not true;

    /// <summary>
    /// Sets the exact document title, rendered verbatim with no company-name suffix (<b>D104</b>). Only the
    /// homepage uses it, because its title is company-authored content (<c>Site:Home:MetaTitle</c>); every other
    /// page keeps <c>ViewData["Title"]</c> and the layout's <c>"{Title} | {company}"</c> form.
    /// </summary>
    public static void SetDocumentTitle(this ViewDataDictionary viewData, string title) =>
        viewData[DocumentTitleKey] = title;

    public static string? DocumentTitle(this ViewDataDictionary viewData) => viewData[DocumentTitleKey] as string;

    /// <summary>
    /// Renders the page body without the layout's content container, so a page can draw full-width bands and
    /// place its own containers inside them (<b>D104</b>). Pages that do not ask keep the container.
    /// </summary>
    public static void SetFullWidth(this ViewDataDictionary viewData) => viewData[FullWidthKey] = true;

    public static bool IsFullWidth(this ViewDataDictionary viewData) => viewData[FullWidthKey] is true;

    private const string SocialImageKey = "SitePage.SocialImage";

    /// <summary>
    /// Declares the page's own photo as its <c>og:image</c> (<b>D106</b>, S5-8) — only when that photo has a social
    /// derivative. There is no site-wide fallback: a page without its own photo declares none.
    /// </summary>
    public static void SetSocialImage(this ViewDataDictionary viewData, MediaImage? image)
    {
        if (image?.Social is not null)
        {
            viewData[SocialImageKey] = image;
        }
    }

    /// <summary>The page's social image, whose <see cref="MediaImage.Social"/> is never <c>null</c>.</summary>
    public static MediaImage? SocialImage(this ViewDataDictionary viewData) => viewData[SocialImageKey] as MediaImage;
}
