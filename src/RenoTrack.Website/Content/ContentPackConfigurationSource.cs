using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace RenoTrack.Website.Content;

/// <summary>
/// One content-pack file as a configuration source that can contribute nothing outside the file's
/// allowed sections (<b>D102</b>).
/// </summary>
internal sealed class ContentPackConfigurationSource(string packRoot, string fileName, bool optional)
    : IConfigurationSource
{
    public string PackRoot { get; } = packRoot;

    public string FileName { get; } = fileName;

    public bool Optional { get; } = optional;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new IsolatedContentPackProvider(this);
}

/// <summary>
/// Reads a content-pack file privately and exposes only its allowed sections.
/// </summary>
/// <remarks>
/// <para>
/// <b>The raw file never becomes application configuration.</b> It is parsed by an inner
/// <see cref="JsonConfigurationProvider"/> that is never added to any configuration builder, so its
/// keys exist only inside <see cref="Load"/>. From there they are first checked against
/// <see cref="ContentPackSectionPolicy"/> — a violation fails startup — and then filtered by
/// construction, so this provider's own <c>Data</c> can only ever hold keys under an allowed root.
/// </para>
/// <para>
/// <b>Do not replace this with <c>AddJsonFile</c>.</b> That would work for every valid pack and remove
/// the only thing standing between a pack author and the application's logging, API origin and
/// forwarder trust list. <c>ContentPackIsolationTests</c> exists to fail when that happens.
/// </para>
/// <para>
/// Never reloaded: content changes take effect on restart, so the site never runs on content that
/// was only half re-validated.
/// </para>
/// </remarks>
internal sealed class IsolatedContentPackProvider(ContentPackConfigurationSource source) : ConfigurationProvider
{
    public override void Load()
    {
        var description = $"'{source.FileName}' in '{source.PackRoot}'";
        var allowedRoots = ContentPackSectionPolicy.AllowedRootsFor(source.FileName);

        IDictionary<string, string?> raw;
        using (var files = new PhysicalFileProvider(source.PackRoot))
        {
            if (!files.GetFileInfo(source.FileName).Exists)
            {
                if (source.Optional)
                {
                    // An absent optional file contributes nothing, which is exactly its meaning. A
                    // present-but-empty one is not absent, and is judged by the policy below.
                    Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                throw new InvalidOperationException(
                    $"Content pack file {description} does not exist, and the pack cannot be loaded without it.");
            }

            var reader = new PrivateJsonProvider(new JsonConfigurationSource
            {
                FileProvider = files,
                Path = source.FileName,
                Optional = false,
                ReloadOnChange = false,
            });

            try
            {
                reader.Load();
            }
            catch (InvalidDataException exception)
            {
                // The file provider wraps every parse failure. The inner message is the parser's own —
                // "Could not parse the JSON file.", a duplicate key's *name*, or a non-object top level —
                // and never carries a value. The full chain stays attached for whoever needs positions.
                throw new InvalidOperationException(
                    $"Content pack file {description} could not be read as JSON configuration: " +
                    $"{exception.InnerException?.Message ?? exception.Message}",
                    exception);
            }

            raw = reader.Snapshot;
        }

        ContentPackSectionPolicy.EnsureOnlyAllowedRoots(description, source.FileName, raw.Keys.ToList(), allowedRoots);

        Data = ContentPackSectionPolicy.Filter(raw, allowedRoots);
    }

    public override string ToString() => $"Content pack '{source.FileName}' ({source.PackRoot})";

    /// <summary>Exists only to read the protected <c>Data</c> of the private parse.</summary>
    private sealed class PrivateJsonProvider(JsonConfigurationSource source) : JsonConfigurationProvider(source)
    {
        public IDictionary<string, string?> Snapshot => Data;
    }
}
