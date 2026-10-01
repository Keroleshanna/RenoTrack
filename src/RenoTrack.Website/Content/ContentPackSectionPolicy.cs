namespace RenoTrack.Website.Content;

/// <summary>
/// Which top-level configuration sections each content-pack file may contribute, and the two
/// mechanisms that hold every file to that list (<b>D102</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The content pack is content, never configuration authority.</b> Its files are loaded as
/// configuration sources above <c>appsettings.json</c>, so without this policy a <c>Logging</c>
/// section in <c>site.json</c> could re-enable the Information-level request and HttpClient logging
/// that <c>CLAUDE.md</c> §24 and <b>D101</b> treat as load-bearing token protections, and a
/// <c>PublicApi</c> or <c>TrustedForwarders</c> section could redirect a customer's token traffic or
/// widen the forwarder trust list. The pack is authored in another repository by another party; it
/// gets no say over product wiring.
/// </para>
/// <para>
/// <b>Two independent mechanisms, deliberately, and neither may be removed because the other
/// exists.</b> <see cref="EnsureOnlyAllowedRoots"/> turns a violation into a startup failure that
/// names the file and the offending sections. <see cref="Filter"/> copies only keys under an allowed
/// root, so an unexpected key is structurally unable to reach application configuration even if the
/// first check were to regress. Each is tested on its own.
/// </para>
/// <para>
/// <b>Keys are judged in their flattened form</b>, which is what configuration actually stores. A
/// JSON property literally named <c>"Logging:LogLevel:Default"</c> flattens to a <c>Logging:…</c> key
/// and is refused at the root, where a check of the JSON object's shape would have accepted it.
/// </para>
/// <para>
/// <b>Messages name files and sections, never values.</b> Someone who pastes a connection string or
/// a signing key into a pack must not have it echoed into an exception or a log.
/// </para>
/// </remarks>
internal static class ContentPackSectionPolicy
{
    /// <summary>The pack's manifest: company identity and the marketing site's content.</summary>
    internal const string SiteFileName = "site.json";

    /// <summary>The Impressum and Datenschutzerklärung (<see cref="LegalContentOptions"/>).</summary>
    internal const string LegalFileName = "legal.json";

    /// <summary>A section name longer than this is shortened in a message rather than echoed whole.</summary>
    private const int MaxEchoedSectionNameLength = 64;

    /// <summary>
    /// The allowed top-level sections per file. Compared case-insensitively, because configuration
    /// keys are case-insensitive: <c>site</c> and <c>Site</c> are the same section to the binder, so
    /// treating them differently here would refuse a spelling that means exactly the allowed thing.
    /// </summary>
    internal static IReadOnlyList<string> AllowedRootsFor(string fileName) => fileName switch
    {
        SiteFileName => [CompanyIdentityOptions.SectionName, SiteOptions.SectionName],
        LegalFileName => [LegalContentOptions.SectionName],
        _ => throw new ArgumentOutOfRangeException(nameof(fileName), fileName, "Not a content-pack file."),
    };

    /// <summary>
    /// Fails when the file's flattened keys name any top-level section outside
    /// <paramref name="allowedRoots"/>, when an allowed root is a scalar or empty rather than a
    /// section with content, or when the file supplies no allowed section at all.
    /// </summary>
    /// <param name="fileDescription">How the file is named in the message, e.g. its name and pack root.</param>
    /// <param name="fileName">The file's own name, used in the guidance text.</param>
    /// <param name="keys">Every key the file produced, in flattened configuration form.</param>
    /// <param name="allowedRoots">The sections this file may contribute.</param>
    /// <exception cref="InvalidOperationException">The file breaks the policy.</exception>
    internal static void EnsureOnlyAllowedRoots(
        string fileDescription,
        string fileName,
        IReadOnlyCollection<string> keys,
        IReadOnlyList<string> allowedRoots)
    {
        var unexpected = keys
            .Select(RootOf)
            .Where(root => !allowedRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException(
                $"Content pack file {fileDescription} contains top-level section(s) " +
                $"{string.Join(", ", unexpected.Select(Quote))}, which it may not set. A content pack " +
                $"supplies content only and cannot change application configuration; '{fileName}' may " +
                $"contain only {string.Join(", ", allowedRoots.Select(Quote))}. Remove the section(s) " +
                "from the pack; product settings belong in the application's own configuration.");
        }

        // A root that is present only as a bare key is a scalar ("Site": "x"), null, or an empty
        // object. None of those is a section with content, and each is far more likely a mistake
        // than an intention.
        var withoutContent = allowedRoots
            .Where(root => keys.Contains(root, StringComparer.OrdinalIgnoreCase)
                && !keys.Any(key => IsUnder(key, root)))
            .ToList();

        if (withoutContent.Count > 0)
        {
            throw new InvalidOperationException(
                $"Content pack file {fileDescription} sets {string.Join(", ", withoutContent.Select(Quote))} " +
                "to a value or an empty object rather than a section with content. Supply the section's " +
                "keys, or remove it.");
        }

        if (!allowedRoots.Any(root => keys.Any(key => IsUnder(key, root))))
        {
            throw new InvalidOperationException(
                $"Content pack file {fileDescription} contains no content. '{fileName}' must contain at " +
                $"least one of {string.Join(", ", allowedRoots.Select(Quote))}; an empty file is refused " +
                "rather than read as a deliberate absence.");
        }
    }

    /// <summary>
    /// Returns only the entries whose key lies under an allowed root. Independent of
    /// <see cref="EnsureOnlyAllowedRoots"/> by design — see the type's remarks.
    /// </summary>
    internal static Dictionary<string, string?> Filter(
        IEnumerable<KeyValuePair<string, string?>> data,
        IReadOnlyList<string> allowedRoots)
    {
        var filtered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in data)
        {
            if (allowedRoots.Any(root => IsUnder(key, root)))
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }

    /// <summary>
    /// Whether <paramref name="key"/> lies strictly beneath <paramref name="root"/>. The bare root key
    /// itself is deliberately excluded: it can only carry a scalar, never a section.
    /// </summary>
    private static bool IsUnder(string key, string root) =>
        key.Length > root.Length + 1
        && key.StartsWith(root, StringComparison.OrdinalIgnoreCase)
        && key[root.Length] == ':';

    private static string RootOf(string key)
    {
        var separator = key.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? key : key[..separator];
    }

    /// <summary>
    /// A section name as it may appear in a message: control characters removed, length capped. It is
    /// a key, not a value, but it still came from a file someone else wrote.
    /// </summary>
    private static string Quote(string section)
    {
        var printable = new string(section.Where(character => !char.IsControl(character)).ToArray());

        return printable.Length <= MaxEchoedSectionNameLength
            ? $"'{printable}'"
            : $"'{printable[..MaxEchoedSectionNameLength]}…'";
    }
}
