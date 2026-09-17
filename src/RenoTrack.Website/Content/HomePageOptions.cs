namespace RenoTrack.Website.Content;

/// <summary>
/// What the homepage presents beyond the company's identity and services, bound from <c>Site:Home</c>
/// (Phase 13 Slice 3, <b>D104</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Only what Slice 3 renders</b> (growth on demand, <c>CLAUDE.md</c> §25). Teasers for projects, the about
/// page and the FAQ arrive with the slices that build those pages.
/// </para>
/// <para>
/// <b><see cref="MetaTitle"/> is the one required field, and only for an enabled site.</b> The homepage's
/// <c>&lt;title&gt;</c> must say what the company does and where it works; the company name alone does not,
/// so there is deliberately no fallback to <see cref="CompanyIdentityOptions.DisplayName"/>. Everything
/// else is optional and omitted when absent — never placeholdered.
/// </para>
/// </remarks>
public sealed class HomePageOptions
{
    public const string SectionName = "Home";

    internal const int MaxMetaTitleLength = 70;
    internal const int MaxHeadlineLength = 90;
    internal const int MaxSubheadlineLength = 160;
    internal const int MinItems = 2;
    internal const int MaxItems = 6;

    /// <summary>The homepage's document title, used verbatim — no company-name suffix is appended.</summary>
    public string? MetaTitle { get; init; }

    /// <summary>The homepage's one <c>h1</c>. Absent: the company name.</summary>
    public string? Headline { get; init; }

    /// <summary>Shown under the headline, and the homepage's meta description. Absent: neither is rendered.</summary>
    public string? Subheadline { get; init; }

    /// <summary>What the company wants a visitor to know about working with it. Empty: section omitted.</summary>
    public IReadOnlyList<HomeItemOptions> Advantages { get; init; } = [];

    /// <summary>How a job proceeds, in order. Empty: section omitted.</summary>
    public IReadOnlyList<HomeItemOptions> Process { get; init; } = [];

    public bool HasMetaTitle => !ContentText.IsBlank(MetaTitle);

    public bool HasHeadline => !ContentText.IsBlank(Headline);

    public bool HasSubheadline => !ContentText.IsBlank(Subheadline);

    /// <exception cref="InvalidOperationException">Supplied content is malformed, naming the key.</exception>
    internal void Validate(string path)
    {
        ValidateOptionalText(MetaTitle, $"{path}:{nameof(MetaTitle)}", MaxMetaTitleLength);
        ValidateOptionalText(Headline, $"{path}:{nameof(Headline)}", MaxHeadlineLength);
        ValidateOptionalText(Subheadline, $"{path}:{nameof(Subheadline)}", MaxSubheadlineLength);

        ValidateItems(Advantages, $"{path}:{nameof(Advantages)}");
        ValidateItems(Process, $"{path}:{nameof(Process)}");
    }

    /// <summary>
    /// Absent is fine; supplied but blank is not. A whitespace-only value is a paste accident that would
    /// otherwise render as an empty title or heading.
    /// </summary>
    private static void ValidateOptionalText(string? value, string key, int maxLength)
    {
        if (value is not null && ContentText.IsBlank(value))
        {
            throw new InvalidOperationException($"Configuration '{key}' is blank. Supply text or remove the key.");
        }

        ContentText.Validate(value, key, maxLength);
    }

    /// <summary>
    /// None, or between <see cref="MinItems"/> and <see cref="MaxItems"/>: a single advantage or a
    /// one-step process is not a list worth a section, and more than six stops being scannable.
    /// </summary>
    private static void ValidateItems(IReadOnlyList<HomeItemOptions> items, string path)
    {
        if (items.Count is (> 0 and < MinItems) or > MaxItems)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}' lists {items.Count} entries; supply none, or between {MinItems} and {MaxItems}.");
        }

        for (var index = 0; index < items.Count; index++)
        {
            items[index].Validate($"{path}:{index}");
        }
    }
}

/// <summary>One entry of a homepage list: a short title and one or two plain sentences.</summary>
public sealed class HomeItemOptions
{
    internal const int MaxTitleLength = 60;
    internal const int MaxTextLength = 200;

    public string? Title { get; init; }

    public string? Text { get; init; }

    internal void Validate(string path)
    {
        Require(Title, $"{path}:{nameof(Title)}", MaxTitleLength);
        Require(Text, $"{path}:{nameof(Text)}", MaxTextLength);
    }

    private static void Require(string? value, string key, int maxLength)
    {
        if (ContentText.IsBlank(value))
        {
            throw new InvalidOperationException($"Configuration '{key}' is required.");
        }

        ContentText.Validate(value, key, maxLength);
    }
}
