namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §9. Grown on demand: <see cref="DeleteAsync"/> was added in Phase 4 Slice 8
/// because compensation after a failed commit needs it. <c>GetAsync</c> still does not exist —
/// nothing reads a stored file back yet (there is no photo-serving endpoint), so it stays unbuilt.
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
}
