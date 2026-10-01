using RenoTrack.Website.Content;

namespace RenoTrack.Website.Site;

/// <summary>
/// The service cards partial's input (<b>D105</b>). The heading level is the calling page's decision, because the
/// same cards sit under an <c>h2</c> on the homepage and directly under the <c>h1</c> on the overview — a shared
/// partial must never fix a level that skips one on either page.
/// </summary>
public sealed record ServiceCardsModel
{
    public ServiceCardsModel(IReadOnlyList<ServiceOptions> services, int headingLevel)
    {
        if (headingLevel is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(headingLevel), headingLevel, "Service card headings are h2 or h3.");
        }

        Services = services;
        HeadingLevel = headingLevel;
    }

    public IReadOnlyList<ServiceOptions> Services { get; }

    public int HeadingLevel { get; }
}

/// <summary>
/// The contact section partial's input (<b>D105</b>). The heading id is the calling page's, so the section's
/// <c>aria-labelledby</c> stays unique and resolvable wherever the partial is used.
/// </summary>
/// <param name="HeadingId">The id of the section's <c>h2</c>, unique on the page.</param>
/// <param name="MailSubject">A <c>mailto:</c> subject, or <c>null</c> for none.</param>
public sealed record ContactSectionModel(string HeadingId, string? MailSubject = null);

/// <summary>The hero's contact actions (<b>D105</b>).</summary>
/// <param name="MailSubject">A <c>mailto:</c> subject, or <c>null</c> for none.</param>
public sealed record HeroActionsModel(string? MailSubject = null);

/// <summary>
/// The hero's text column (<b>D106</b>): shared by the text-only hero and the split hero, so the two layouts can
/// never drift apart in what they say.
/// </summary>
/// <param name="HeadingId">The id of the page's one <c>h1</c>.</param>
/// <param name="Lead">The lead paragraph, or <c>null</c> for none.</param>
public sealed record HeroTextModel(string HeadingId, string Headline, string? Lead, HeroActionsModel Actions);

/// <summary>How an image loads (<b>D106</b>).</summary>
public enum PictureLoading
{
    /// <summary>The page's largest content above the fold: never lazy, <c>fetchpriority="high"</c>.</summary>
    Priority,

    /// <summary>Everything else: <c>loading="lazy"</c>.</summary>
    Lazy,
}

/// <summary>One responsive photo: WebP first, JPEG fallback, explicit dimensions (<b>D106</b>).</summary>
/// <param name="Sizes">One of <see cref="PictureSizes"/>, matching the CSS layout the picture sits in.</param>
public sealed record PictureModel(MediaImage Image, string Sizes, PictureLoading Loading);

/// <summary>
/// The <c>sizes</c> attribute for each layout that shows a photo (<b>D106</b>). Each mirrors <c>marketing.css</c>:
/// changing a column width there means changing it here, and browser QA checks the chosen <c>currentSrc</c> against
/// the rendered width.
/// </summary>
public static class PictureSizes
{
    /// <summary>
    /// From 1024 px the image column of the layered hero: 7 of 12 tracks of the wide container after the gap, at
    /// most 42rem. Below, the full container width, whose padding is 1.5rem from 480 px and 1rem below.
    /// </summary>
    /// <remarks>
    /// Re-measured in Slice 5v (<b>D107</b>), when the hero's columns changed from 1.1fr/1fr to 5fr/7fr and the
    /// split hero widened its container. The derivatives themselves are unchanged: this is the <c>sizes</c>
    /// attribute only, and browser QA checks the chosen <c>currentSrc</c> against the rendered width.
    /// </remarks>
    public const string Hero = "(min-width: 1024px) min(42rem, 54vw), (min-width: 480px) calc(100vw - 3rem), calc(100vw - 2rem)";

    /// <summary>A service card: a third of the container from 1024 px, half from 768 px, full width below.</summary>
    public const string Card = "(min-width: 1024px) min(22rem, 30vw), (min-width: 768px) calc(50vw - 2.25rem), calc(100vw - 2rem)";
}
