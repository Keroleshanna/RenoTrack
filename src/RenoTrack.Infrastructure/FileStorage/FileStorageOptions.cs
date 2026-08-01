namespace RenoTrack.Infrastructure.FileStorage;

/// <summary>
/// Local-disk storage settings, bound from the <c>FileStorage</c> configuration section
/// (Architecture.md §9).
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Directory under which every stored file lives. Also the boundary
    /// <see cref="LocalDiskFileStorage"/> refuses to let any resolved path escape.
    /// </summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>
    /// Fails startup naming the exact configuration key at fault, matching the connection-string
    /// and JWT checks — a missing root must not first surface as a failed photo upload in
    /// production.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(RootPath)}' is required.");
        }
    }
}
