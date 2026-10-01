using System.Globalization;

namespace RenoTrack.Website.Content;

/// <summary>One block of regular opening hours: the days it applies to, and one opening span.</summary>
/// <remarks>
/// <para>
/// <b>Only what structured data can state honestly.</b> Regular, repeating hours go here; anything
/// that is not a fixed span — "Saturday by appointment" — belongs in
/// <see cref="CompanyIdentityOptions.OpeningHoursNote"/>, which is shown to people and never
/// published as machine-readable hours (Phase 13 addendum §B).
/// </para>
/// <para>
/// Days are the English <see cref="DayOfWeek"/> names and are parsed here rather than by the binder,
/// which would also accept <c>"1"</c> or <c>"monday"</c> and report a failure with a message of its own.
/// </para>
/// </remarks>
public sealed class OpeningHoursOptions
{
    public IReadOnlyList<string> Days { get; init; } = [];

    /// <summary>24-hour <c>HH:mm</c>, e.g. <c>08:00</c>.</summary>
    public string? Opens { get; init; }

    /// <summary>24-hour <c>HH:mm</c>, later than <see cref="Opens"/>.</summary>
    public string? Closes { get; init; }

    internal const string TimeFormat = "HH:mm";

    /// <summary>The parsed days, once validated.</summary>
    public IEnumerable<DayOfWeek> DaysOfWeek => Days.Select(day => Enum.Parse<DayOfWeek>(day.Trim()));

    internal void Validate(string path)
    {
        if (Days.Count == 0)
        {
            throw new InvalidOperationException($"Configuration '{path}:{nameof(Days)}' must name at least one day.");
        }

        for (var index = 0; index < Days.Count; index++)
        {
            var day = Days[index]?.Trim();
            if (string.IsNullOrEmpty(day) || !Enum.GetNames<DayOfWeek>().Contains(day, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configuration '{path}:{nameof(Days)}:{index}' must be an English day name such as " +
                    "'Monday', written exactly so.");
            }
        }

        if (DaysOfWeek.Distinct().Count() != Days.Count)
        {
            throw new InvalidOperationException($"Configuration '{path}:{nameof(Days)}' names the same day twice.");
        }

        var opens = ParseTime(Opens, $"{path}:{nameof(Opens)}");
        var closes = ParseTime(Closes, $"{path}:{nameof(Closes)}");

        if (opens >= closes)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Closes)}' must be later than '{path}:{nameof(Opens)}'.");
        }
    }

    private static TimeOnly ParseTime(string? value, string key)
    {
        if (!TimeOnly.TryParseExact(value?.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be a 24-hour time written as '{TimeFormat}', e.g. '08:00'.");
        }

        return time;
    }
}
