using System.Text.RegularExpressions;

namespace RenoTrack.Website.Content;

/// <summary>One service the company offers, as the marketing site presents it.</summary>
/// <remarks>
/// <para>
/// <b>Only what Slices 2–4 render</b> (growth on demand, <c>CLAUDE.md</c> §4/§7). Images arrive with the
/// media slice, service-specific questions with the FAQ, and the inquiry's service type with the inquiry
/// flow — each in the slice that first uses it.
/// </para>
/// <para>
/// <b>The slug is a URL segment</b> under <c>/leistungen/</c>, so it is lowercase ASCII with single
/// hyphens: a German name such as <c>Türen</c> is written <c>tueren</c> by the company, not transliterated
/// by code, because a URL is a decision the company owns and must be able to keep stable.
/// </para>
/// <para>
/// <b>The service page (Slice 4, D105)</b> adds a verbatim <see cref="MetaTitle"/> (required once the site is
/// enabled, checked by <see cref="SiteOptions"/>), an optional <see cref="Headline"/> and
/// <see cref="MetaDescription"/>, and optional descriptive <see cref="Sections"/>. <see cref="Summary"/> stays the
/// card text and the page's lead; it is deliberately not the meta description, because code never shortens
/// company copy to fit one.
/// </para>
/// </remarks>
public sealed partial class ServiceOptions
{
    internal const int MaxSlugLength = 40;
    internal const int MaxNameLength = 60;
    internal const int MaxSummaryLength = 300;
    internal const int MaxOfferingLength = 160;
    internal const int MaxOfferings = 12;
    internal const int MaxMetaTitleLength = 70;
    internal const int MaxHeadlineLength = 90;
    internal const int MaxMetaDescriptionLength = 160;
    internal const int MaxSections = 4;

    public string? Slug { get; init; }

    public string? Name { get; init; }

    /// <summary>One or two plain sentences saying what the service is.</summary>
    public string? Summary { get; init; }

    /// <summary>What the company actually offers within this service, one item per entry.</summary>
    public IReadOnlyList<string> Offerings { get; init; } = [];

    /// <summary>The service page's document title, used verbatim — no company-name suffix is appended.</summary>
    public string? MetaTitle { get; init; }

    /// <summary>The service page's one <c>h1</c>. Absent: <see cref="Name"/>.</summary>
    public string? Headline { get; init; }

    /// <summary>The service page's meta description. Absent: none is rendered.</summary>
    public string? MetaDescription { get; init; }

    /// <summary>Descriptive text blocks on the service page. Empty: none rendered.</summary>
    public IReadOnlyList<ServiceSectionOptions> Sections { get; init; } = [];

    /// <summary>
    /// The <c>Site:Media</c> id of this service's photo (Slice 5a, D106). Absent: the service page keeps its
    /// text-only hero, and no service card on the site shows a photo (S5-7).
    /// </summary>
    public string? Image { get; init; }

    public bool HasMetaTitle => !ContentText.IsBlank(MetaTitle);

    public bool HasImage => !ContentText.IsBlank(Image);

    public bool HasHeadline => !ContentText.IsBlank(Headline);

    public bool HasMetaDescription => !ContentText.IsBlank(MetaDescription);

    internal void Validate(string path)
    {
        if (ContentText.IsBlank(Slug) || Slug!.Length > MaxSlugLength || !SlugPattern().IsMatch(Slug))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Slug)}' must be lowercase ASCII letters and digits separated " +
                $"by single hyphens, at most {MaxSlugLength} characters, e.g. 'innenausbau' or 'tueren'.");
        }

        if (ContentText.IsBlank(Name))
        {
            throw new InvalidOperationException($"Configuration '{path}:{nameof(Name)}' is required.");
        }

        ContentText.Validate(Name, $"{path}:{nameof(Name)}", MaxNameLength);

        if (ContentText.IsBlank(Summary))
        {
            throw new InvalidOperationException($"Configuration '{path}:{nameof(Summary)}' is required.");
        }

        ContentText.Validate(Summary, $"{path}:{nameof(Summary)}", MaxSummaryLength);

        if (Offerings.Count == 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Offerings)}' must list at least one offering.");
        }

        // A scope list longer than this stops being scannable on the page (D105, S4-4).
        if (Offerings.Count > MaxOfferings)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Offerings)}' lists {Offerings.Count} offerings; the limit is {MaxOfferings}.");
        }

        for (var index = 0; index < Offerings.Count; index++)
        {
            var key = $"{path}:{nameof(Offerings)}:{index}";
            if (ContentText.IsBlank(Offerings[index]))
            {
                throw new InvalidOperationException($"Configuration '{key}' is blank.");
            }

            ContentText.Validate(Offerings[index], key, MaxOfferingLength);
        }

        ContentText.ValidateOptional(MetaTitle, $"{path}:{nameof(MetaTitle)}", MaxMetaTitleLength);
        ContentText.ValidateOptional(Headline, $"{path}:{nameof(Headline)}", MaxHeadlineLength);
        ContentText.ValidateOptional(MetaDescription, $"{path}:{nameof(MetaDescription)}", MaxMetaDescriptionLength);

        // Only blank-ness here; whether the id names a listed photo is SiteOptions' check.
        ContentText.ValidateOptional(Image, $"{path}:{nameof(Image)}", MediaItemOptions.MaxIdLength);

        if (Sections.Count > MaxSections)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Sections)}' lists {Sections.Count} sections; supply none, or at most {MaxSections}.");
        }

        for (var index = 0; index < Sections.Count; index++)
        {
            Sections[index].Validate($"{path}:{nameof(Sections)}:{index}");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

/// <summary>One descriptive block of a service page: a heading and one to four plain paragraphs (D105).</summary>
public sealed class ServiceSectionOptions
{
    internal const int MaxHeadingLength = 80;
    internal const int MaxParagraphLength = 600;
    internal const int MaxParagraphs = 4;

    public string? Heading { get; init; }

    /// <summary>Single-line plain text each; a line break is refused like everywhere else in the pack.</summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    internal void Validate(string path)
    {
        ContentText.ValidateRequired(Heading, $"{path}:{nameof(Heading)}", MaxHeadingLength);

        if (Paragraphs.Count is 0 or > MaxParagraphs)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Paragraphs)}' lists {Paragraphs.Count} paragraphs; supply between 1 and {MaxParagraphs}.");
        }

        for (var index = 0; index < Paragraphs.Count; index++)
        {
            ContentText.ValidateRequired(Paragraphs[index], $"{path}:{nameof(Paragraphs)}:{index}", MaxParagraphLength);
        }
    }
}
