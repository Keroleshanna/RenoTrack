namespace RenoTrack.Domain.Entities;

/// <summary>
/// A photo attached to an Inspection. Child entity of the Inspection aggregate (Architecture.md
/// §6) — only ever created through <see cref="Inspection.AddPhoto"/>, never directly, which is
/// why the constructor is <c>internal</c> rather than <c>public</c>.
///
/// Deliberately holds only domain-meaningful data: an opaque <see cref="FileUrl"/> reference,
/// an optional caption, and an upload timestamp. This type has zero knowledge of how or where
/// the underlying file bytes are actually stored — <see cref="FileUrl"/> is populated by the
/// Application layer only after it has already called <c>IFileStorage.SaveAsync(...)</c>
/// (Architecture.md §9), so RenoTrack.Domain never needs to reference any file storage
/// technology or package.
/// </summary>
public sealed class InspectionPhoto
{
    public int Id { get; private set; }
    public string FileUrl { get; private set; }
    public string? Caption { get; private set; }
    public DateTime UploadedAt { get; private set; }

    internal InspectionPhoto(string fileUrl, string? caption)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("File URL is required.", nameof(fileUrl));

        FileUrl = fileUrl;
        Caption = caption?.Trim();
        UploadedAt = DateTime.UtcNow;
    }
}
