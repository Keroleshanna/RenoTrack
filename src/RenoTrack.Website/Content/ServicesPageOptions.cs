namespace RenoTrack.Website.Content;

/// <summary>
/// What the services overview at <c>/leistungen</c> presents beyond the services themselves, bound from
/// <c>Site:ServicesPage</c> (Phase 13 Slice 4, <b>D105</b>).
/// </summary>
/// <remarks>
/// <b><see cref="MetaTitle"/> is required for an enabled site, with no fallback</b> — the homepage's reasoning
/// (D104 C1): <c>"Leistungen | {company}"</c> says neither what the company does nor where it works, and the
/// overview is a page people reach by searching for exactly that. Everything else is optional and omitted when
/// absent, never placeholdered.
/// </remarks>
public sealed class ServicesPageOptions
{
    public const string SectionName = "ServicesPage";

    internal const int MaxMetaTitleLength = 70;
    internal const int MaxHeadlineLength = 90;
    internal const int MaxIntroLength = 160;

    /// <summary>The overview's document title, used verbatim — no company-name suffix is appended.</summary>
    public string? MetaTitle { get; init; }

    /// <summary>The overview's one <c>h1</c>. Absent: the product label "Leistungen".</summary>
    public string? Headline { get; init; }

    /// <summary>Shown under the headline, and the overview's meta description. Absent: neither is rendered.</summary>
    public string? Intro { get; init; }

    public bool HasMetaTitle => !ContentText.IsBlank(MetaTitle);

    public bool HasHeadline => !ContentText.IsBlank(Headline);

    public bool HasIntro => !ContentText.IsBlank(Intro);

    /// <exception cref="InvalidOperationException">Supplied content is malformed, naming the key.</exception>
    internal void Validate(string path)
    {
        ContentText.ValidateOptional(MetaTitle, $"{path}:{nameof(MetaTitle)}", MaxMetaTitleLength);
        ContentText.ValidateOptional(Headline, $"{path}:{nameof(Headline)}", MaxHeadlineLength);
        ContentText.ValidateOptional(Intro, $"{path}:{nameof(Intro)}", MaxIntroLength);
    }
}
