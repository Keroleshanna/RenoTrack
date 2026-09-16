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
/// </remarks>
public sealed partial class ServiceOptions
{
    internal const int MaxSlugLength = 40;
    internal const int MaxNameLength = 60;
    internal const int MaxSummaryLength = 300;
    internal const int MaxOfferingLength = 160;

    public string? Slug { get; init; }

    public string? Name { get; init; }

    /// <summary>One or two plain sentences saying what the service is.</summary>
    public string? Summary { get; init; }

    /// <summary>What the company actually offers within this service, one item per entry.</summary>
    public IReadOnlyList<string> Offerings { get; init; } = [];

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

        for (var index = 0; index < Offerings.Count; index++)
        {
            var key = $"{path}:{nameof(Offerings)}:{index}";
            if (ContentText.IsBlank(Offerings[index]))
            {
                throw new InvalidOperationException($"Configuration '{key}' is blank.");
            }

            ContentText.Validate(Offerings[index], key, MaxOfferingLength);
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
