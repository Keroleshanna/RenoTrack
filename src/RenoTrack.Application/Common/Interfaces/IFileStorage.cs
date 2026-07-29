namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §9. Deliberately starts with only <see cref="SaveAsync"/> —
/// <c>GetAsync</c>/<c>DeleteAsync</c> are added when a real command/query requires them.
///
/// The caller determines <c>fileUrl</c> up front (e.g. a GUID-based key) rather than this
/// method generating and returning one. This lets a handler check a Domain invariant against
/// the already-known key (e.g. <c>Inspection.AddPhoto(fileUrl, ...)</c>, which enforces BR-10)
/// *before* performing the actual, irreversible I/O — if the Domain rejects it, this method is
/// never called at all, instead of uploading first and discovering the rejection afterward.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken);
}
