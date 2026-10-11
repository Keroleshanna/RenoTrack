namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §9. Grown on demand: <see cref="DeleteAsync"/> was added in Phase 4 Slice 8
/// because compensation after a failed commit needs it, and <see cref="OpenReadAsync"/> in Phase 14
/// Slice 3a because the archived invoice document is read back — for the Admin's download and for an
/// email retry (D113). Inspection photos still have no serving endpoint; that gap is unchanged.
///
/// The caller determines <c>fileUrl</c> up front (e.g. a GUID-based key) rather than this
/// method generating and returning one. This lets a handler check a Domain invariant against
/// the already-known key (e.g. <c>Inspection.AddPhoto(fileUrl, ...)</c>, which enforces BR-10)
/// *before* performing the actual, irreversible I/O — if the Domain rejects it, this method is
/// never called at all, instead of uploading first and discovering the rejection afterward.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Writes <paramref name="content"/> at <paramref name="fileUrl"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must treat <paramref name="fileUrl"/> as untrusted. This contract is broader
    /// than any one caller: today's only caller composes the key from controlled components, but the
    /// parameter is a plain string, so an implementation backed by a storage root must verify that
    /// the resolved destination stays inside it rather than relying on callers to behave.
    /// <para>
    /// <b>A save that throws must not leave a partially written file at <paramref name="fileUrl"/></b>
    /// that could later be read as successfully stored — the archived invoice relies on it (D113).
    /// Implementations never overwrite an existing file.
    /// </para>
    /// </remarks>
    Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the file at <paramref name="fileUrl"/> if it exists; does nothing if it does not.
    /// </summary>
    /// <remarks>
    /// Deliberately idempotent, because its one caller is a compensation path running after another
    /// failure — where "the file was never written" and "the file is already gone" are both fine
    /// outcomes, and neither should raise a second exception on top of the one being handled.
    /// </remarks>
    Task DeleteAsync(string fileUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the file at <paramref name="fileUrl"/> for reading. The caller disposes the stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A missing file throws, and that is a defect, never a cue to recreate it.</b> Its first
    /// caller reads an issued invoice's archived document, which BR-15 says is never regenerated: a
    /// key recorded on an aggregate whose file is gone means storage lost evidence, and the honest
    /// answer is a failure, not a freshly rendered substitute.
    /// </para>
    /// <para>
    /// Implementations validate <paramref name="fileUrl"/> exactly as <see cref="SaveAsync"/> does,
    /// and the exception for a missing file must not repeat the key or a resolved path.
    /// </para>
    /// </remarks>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="fileUrl"/>.</exception>
    Task<Stream> OpenReadAsync(string fileUrl, CancellationToken cancellationToken);
}
