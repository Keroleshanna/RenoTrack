using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace RenoTrack.Website.Content;

/// <summary>
/// Where the company's content pack lives, bound from the <c>ContentPack</c> configuration section
/// (<b>D102</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The pack lives outside this repository and outside the application</b> — in the company's own
/// private content repository (Phase 13 Q9) — so the product stays reusable for another company
/// without a fork, and no company identity or legal text is committed here (<b>D100</b>, unchanged).
/// </para>
/// <para>
/// <b>Optional, but wiring once set.</b> Absent, the site behaves exactly as before the pack existed:
/// identity and legal text come from ordinary configuration and the logo from <c>brand/</c> beside the
/// application. Set, a path that is relative, missing, or lacks <c>site.json</c> fails startup naming
/// this key — a deployment that names a pack and silently runs without it would look configured and
/// not be.
/// </para>
/// <para>
/// Read from the host's own configuration, never from the pack itself: a pack cannot name its own
/// location, and <c>ContentPack</c> is not among any pack file's allowed sections.
/// </para>
/// </remarks>
public sealed class ContentPackOptions
{
    public const string SectionName = "ContentPack";

    /// <summary>The pack directory's absolute path.</summary>
    public string? RootPath { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RootPath);

    /// <summary>The validated, fully-qualified pack directory. Only meaningful once <see cref="Validate"/> has passed.</summary>
    public string ResolvedRootPath => Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath!.Trim()));

    /// <summary>The logo mount's directory: the pack's <c>brand/</c> when a pack is configured.</summary>
    public string BrandRootFor(string contentRootPath) => Path.Combine(
        IsConfigured ? ResolvedRootPath : contentRootPath,
        CompanyIdentityOptions.BrandAssetsDirectoryName);

    /// <summary>
    /// The published photo derivatives' directory (Slice 5a, D106): the pack's <c>media/</c> when a pack is
    /// configured, otherwise <c>media/</c> beside the application. Never mounted as a directory — only files the
    /// validated manifest names are served (<c>MediaCatalog</c>).
    /// </summary>
    public string MediaRootFor(string contentRootPath) => Path.Combine(
        IsConfigured ? ResolvedRootPath : contentRootPath,
        MediaDirectoryName);

    /// <summary>The name of the directory holding published photo derivatives.</summary>
    public const string MediaDirectoryName = "media";

    /// <exception cref="InvalidOperationException">The configured path is unusable as a pack.</exception>
    public void Validate()
    {
        if (!IsConfigured)
        {
            return;
        }

        var key = $"{SectionName}:{nameof(RootPath)}";
        var path = RootPath!.Trim();

        // Relative paths resolve against the process's working directory, which differs between a
        // service host, a container and a developer's shell — the same setting would name a different
        // directory on each.
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' has value '{RootPath}', which is not an absolute path. A content " +
                "pack is located by an absolute path, because a relative one resolves against whatever " +
                "the process's working directory happens to be.");
        }

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' names '{RootPath}', which is not an existing directory.");
        }

        if (!File.Exists(Path.Combine(path, ContentPackSectionPolicy.SiteFileName)))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' names '{RootPath}', which contains no " +
                $"'{ContentPackSectionPolicy.SiteFileName}'. A content pack without its manifest is broken, " +
                "not empty.");
        }
    }

    /// <summary>
    /// Inserts the pack's two files into <paramref name="sources"/> at the agreed precedence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Above every file source, below the first environment-variable or command-line source that
    /// follows them.</b> File sources are <c>appsettings.json</c>, <c>appsettings.{Environment}.json</c>
    /// and user-secrets, so the pack's content overrides them; environment variables and command-line
    /// arguments still override the pack, so an operator can correct one value without editing the
    /// company's repository. <c>WebApplication.CreateBuilder</c> also registers <c>DOTNET_</c>- and
    /// <c>ASPNETCORE_</c>-prefixed environment sources <em>before</em> the file sources, as host
    /// settings — which is why the position is found from the last file source rather than from the
    /// first environment source.
    /// </para>
    /// <para>
    /// <b>The precedence governs content sections only.</b> Every key outside a file's allowed roots is
    /// refused or filtered before it exists (<see cref="IsolatedContentPackProvider"/>).
    /// </para>
    /// </remarks>
    public void AddTo(IList<IConfigurationSource> sources)
    {
        if (!IsConfigured)
        {
            return;
        }

        var lastFileSource = -1;
        for (var index = 0; index < sources.Count; index++)
        {
            if (sources[index] is FileConfigurationSource)
            {
                lastFileSource = index;
            }
        }

        var position = lastFileSource + 1;

        // With no file source at all, sit below the first environment or command-line source, so
        // both still win.
        if (lastFileSource < 0)
        {
            var firstOverride = Enumerable.Range(0, sources.Count).FirstOrDefault(
                index => sources[index] is EnvironmentVariablesConfigurationSource or CommandLineConfigurationSource,
                sources.Count);
            position = firstOverride;
        }

        sources.Insert(position, new ContentPackConfigurationSource(
            ResolvedRootPath, ContentPackSectionPolicy.LegalFileName, optional: true));
        sources.Insert(position, new ContentPackConfigurationSource(
            ResolvedRootPath, ContentPackSectionPolicy.SiteFileName, optional: false));
    }
}
