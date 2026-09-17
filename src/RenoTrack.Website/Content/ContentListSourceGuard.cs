namespace RenoTrack.Website.Content;

/// <summary>
/// Refuses a content list that is supplied by more than one configuration source (<b>D102</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>.NET configuration merges arrays by index across sources, silently.</b> A leftover
/// <c>Site:Services:0:Name</c> in a developer's git-ignored <c>appsettings.Development.json</c> would
/// not add a service or be ignored — it would overwrite the <em>name</em> of the pack's first
/// service and leave its slug, summary and offerings in place, producing a service page that belongs
/// to neither source. Nothing downstream can tell that apart from a deliberate entry.
/// </para>
/// <para>
/// <b>Lists only.</b> A single scalar override — an environment variable correcting
/// <c>CompanyIdentity:ContactPhone</c> — replaces a value whole and is a legitimate operator action,
/// so it stays allowed.
/// </para>
/// </remarks>
internal static class ContentListSourceGuard
{
    /// <summary>The list sections that must each come from exactly one configuration source.</summary>
    internal static readonly IReadOnlyList<string> ListSections =
    [
        $"{SiteOptions.SectionName}:{nameof(SiteOptions.Services)}",
        $"{SiteOptions.SectionName}:{nameof(SiteOptions.Home)}:{nameof(HomePageOptions.Advantages)}",
        $"{SiteOptions.SectionName}:{nameof(SiteOptions.Home)}:{nameof(HomePageOptions.Process)}",
        $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.OpeningHours)}",
        $"{CompanyIdentityOptions.SectionName}:{nameof(CompanyIdentityOptions.ServiceArea)}:{nameof(ServiceAreaOptions.Places)}",
    ];

    /// <exception cref="InvalidOperationException">A list section has keys in more than one provider.</exception>
    internal static void EnsureSingleSource(IConfigurationRoot configuration)
    {
        foreach (var section in ListSections)
        {
            var suppliers = configuration.Providers
                .Where(provider => provider.GetChildKeys([], section).Any())
                .ToList();

            if (suppliers.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Configuration '{section}' is supplied by more than one configuration source: " +
                    $"{string.Join("; ", suppliers.Select(provider => provider.ToString()))}. Configuration " +
                    "merges lists entry by entry across sources, so the result would silently mix both. " +
                    "Supply the whole list from one source and remove it from the others.");
            }
        }
    }
}
