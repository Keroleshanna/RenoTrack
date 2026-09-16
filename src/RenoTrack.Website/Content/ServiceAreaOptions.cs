namespace RenoTrack.Website.Content;

/// <summary>Where the company works: named places, and an optional note about exceptions.</summary>
public sealed class ServiceAreaOptions
{
    internal const int MaxNoteLength = 300;

    public IReadOnlyList<ServiceAreaPlaceOptions> Places { get; init; } = [];

    /// <summary>Shown to people, e.g. that larger projects may be taken on further away.</summary>
    public string? Note { get; init; }

    public bool HasPlaces => Places.Count > 0;

    internal void Validate(string path)
    {
        ContentText.Validate(Note, $"{path}:{nameof(Note)}", MaxNoteLength);

        for (var index = 0; index < Places.Count; index++)
        {
            Places[index].Validate($"{path}:{nameof(Places)}:{index}");
        }

        var duplicate = Places
            .GroupBy(place => place.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Places)}' names '{duplicate.Key}' more than once.");
        }
    }
}

/// <summary>One named place in the service area.</summary>
public sealed class ServiceAreaPlaceOptions
{
    internal const int MaxNameLength = 80;

    public string? Name { get; init; }

    /// <summary><c>City</c> for a town, <c>Region</c> for a wider area such as a landscape or district.</summary>
    public string? Kind { get; init; }

    public const string CityKind = "City";

    public const string RegionKind = "Region";

    internal void Validate(string path)
    {
        if (ContentText.IsBlank(Name))
        {
            throw new InvalidOperationException($"Configuration '{path}:{nameof(Name)}' is required.");
        }

        ContentText.Validate(Name, $"{path}:{nameof(Name)}", MaxNameLength);

        if (!string.Equals(Kind?.Trim(), CityKind, StringComparison.Ordinal)
            && !string.Equals(Kind?.Trim(), RegionKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(Kind)}' must be '{CityKind}' or '{RegionKind}'.");
        }
    }
}
