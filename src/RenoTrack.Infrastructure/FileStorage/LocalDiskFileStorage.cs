using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.FileStorage;

/// <summary>
/// Disk-backed <see cref="IFileStorage"/> (Architecture.md §9), replacing Phase 3's throwing
/// placeholder. Files live under a configured root in whatever sub-path the caller's key describes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Root containment is enforced here regardless of the caller.</b> Today's only caller composes
/// its key from controlled components — a GUID plus a validated extension — so no caller-supplied
/// path segment currently reaches this class. That was verified rather than assumed: hostile
/// filenames such as <c>../../evil.jpg</c> are reduced by <c>Path.GetExtension</c> to <c>.jpg</c>,
/// which structurally cannot contain a directory separator. But <see cref="IFileStorage.SaveAsync"/>
/// takes a plain string, so the contract is broader than that one caller, and a future one could
/// pass anything. Verifying containment at the storage boundary means safety does not depend on
/// every caller continuing to behave.
/// </para>
/// <para>
/// Both entry points resolve the destination and prove it stays under the root before touching the
/// filesystem. Rooted keys such as <c>/etc/passwd</c> or <c>C:\Windows\evil.exe</c> are rejected for
/// the same reason as <c>../</c> traversal: <c>Path.Combine</c> discards the root when the second
/// argument is absolute, so the result escapes.
/// </para>
/// </remarks>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(FileStorageOptions options)
    {
        // Canonicalized once so every later comparison comes from the same normal form; a relative
        // or unnormalized configured root would otherwise make containment checks unreliable.
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
    }

    public async Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken)
    {
        var destination = ResolveWithinRoot(fileUrl);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // FileMode.CreateNew, never Create: an existing file means a GUID collision or a bug, and
        // silently overwriting would destroy evidence attached to an Inspection. Failing loudly is
        // the correct response to something that should be impossible.
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(target, cancellationToken);
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken)
    {
        var destination = ResolveWithinRoot(fileUrl);

        try
        {
            File.Delete(destination);
        }
        catch (DirectoryNotFoundException)
        {
            // File.Delete is only *partly* idempotent, which a test caught rather than a reading of
            // the docs: it no-ops for a missing file but throws when a parent directory is missing.
            // The compensation caller reaches here precisely when something already went wrong, so
            // "the directory was never created" must be as acceptable as "the file is already gone".
            // Caught rather than pre-checked with Directory.Exists, which would be racy.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves <paramref name="fileUrl"/> against the root and proves the result stays inside it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The key is empty, or resolves outside the root. <c>ArgumentException</c> rather than a custom
    /// type because this signals a caller passing a key it had no business passing — a programming
    /// error, not a business outcome — and the ProblemDetails middleware maps it to 400 rather than
    /// leaking a path in a 500.
    /// </exception>
    private string ResolveWithinRoot(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            throw new ArgumentException("A file key is required.", nameof(fileUrl));
        }

        string resolved;

        try
        {
            resolved = Path.GetFullPath(Path.Combine(_root, fileUrl));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Path.Combine/GetFullPath throw on characters the platform cannot represent in a path
            // (a NUL, for instance). Rephrased rather than propagated, so the message never repeats
            // the offending key back.
            throw new ArgumentException("The file key is not a valid storage path.", nameof(fileUrl));
        }

        // Comparing against root + separator, not root alone, so a sibling directory whose name
        // merely starts with the root's (".../storage-backup") cannot pass as contained.
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The file key resolves outside the storage root.", nameof(fileUrl));
        }

        return resolved;
    }
}
