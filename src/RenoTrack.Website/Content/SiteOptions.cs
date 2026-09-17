namespace RenoTrack.Website.Content;

/// <summary>
/// The public marketing site: its canonical origin and the services it presents, bound from the
/// <c>Site</c> configuration section (<b>D102</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="PublicBaseUrl"/> is the switch.</b> Absent, there is no marketing site — the Website
/// serves the customer token pages and the legal pages exactly as before, and one startup warning says
/// so. Present, the site is enabled and the company facts it cannot be published without become
/// required: a marketing site never starts with half an identity. This mirrors <b>D100</b>'s
/// "absence means the route does not exist", applied to a whole site rather than to one page.
/// </para>
/// <para>
/// <b>Services are validated whenever they are supplied</b>, enabled or not — malformed content is never
/// silently accepted — but only an enabled site requires at least one.
/// </para>
/// </remarks>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// The canonical public origin, e.g. <c>https://www.example.test</c> — scheme and host (and a port,
    /// if one is genuinely needed), nothing else.
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    public IReadOnlyList<ServiceOptions> Services { get; init; } = [];

    /// <summary>
    /// The company's brand colours (added in Phase 13 Slice 2, D103). Validated whenever supplied; absent
    /// values fall back to neutral product defaults.
    /// </summary>
    public ThemeOptions Theme { get; init; } = new();

    public bool IsEnabled => !ContentText.IsBlank(PublicBaseUrl);

    /// <summary>
    /// <see cref="PublicBaseUrl"/> normalised — host lower-cased, no trailing slash — for building
    /// absolute URLs. Only meaningful once <see cref="Validate"/> has passed on an enabled site.
    /// </summary>
    public string CanonicalOrigin => new Uri(PublicBaseUrl!.Trim(), UriKind.Absolute).GetLeftPart(UriPartial.Authority);

    /// <exception cref="InvalidOperationException">Supplied content is malformed, or an enabled site lacks what it requires.</exception>
    public void Validate(CompanyIdentityOptions identity)
    {
        for (var index = 0; index < Services.Count; index++)
        {
            Services[index].Validate($"{SectionName}:{nameof(Services)}:{index}");
        }

        EnsureUnique(service => service.Slug!, nameof(ServiceOptions.Slug), StringComparer.Ordinal);
        EnsureUnique(service => service.Name!.Trim(), nameof(ServiceOptions.Name), StringComparer.OrdinalIgnoreCase);

        Theme.Validate($"{SectionName}:{nameof(Theme)}");

        if (!IsEnabled)
        {
            return;
        }

        ValidatePublicBaseUrl();

        var missing = identity.MissingForMarketingSite().ToList();
        if (Services.Count == 0)
        {
            missing.Add($"{SectionName}:{nameof(Services)}");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(PublicBaseUrl)}' enables the marketing site, which cannot " +
                $"be published without: {string.Join(", ", missing.Select(key => $"'{key}'"))}. Supply them, or " +
                $"remove '{SectionName}:{nameof(PublicBaseUrl)}' to run without the marketing site.");
        }
    }

    private void ValidatePublicBaseUrl()
    {
        var key = $"{SectionName}:{nameof(PublicBaseUrl)}";
        var value = PublicBaseUrl!.Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has value '{PublicBaseUrl}', which is not an absolute URL. Expected the " +
                "site's canonical origin, e.g. 'https://www.example.test'.");
        }

        // The same rule PublicApi:BaseUrl and TokenLink:PublicBaseUrl enforce: HTTPS in every environment.
        // Every canonical URL, sitemap entry and structured-data identifier is built from this value.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has scheme '{uri.Scheme}', but the canonical origin must be HTTPS.");
        }

        // Checked on the raw string as well as on the parsed URI: a bare trailing '?' or '#' parses to
        // an empty query or fragment and would otherwise pass unnoticed.
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || value.Contains('?', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has value '{PublicBaseUrl}', but must be an origin only — scheme and host, " +
                "with no path, query, fragment or user information.");
        }
    }

    private void EnsureUnique(Func<ServiceOptions, string> selector, string property, StringComparer comparer)
    {
        var duplicate = Services.GroupBy(selector, comparer).FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(Services)}' has more than one service with " +
                $"{property} '{duplicate.Key}'.");
        }
    }
}
