namespace RenoTrack.Api.Inspections.Dtos;

/// <summary>
/// Multipart payload for uploading an Inspection photo (SRS FR-3.2).
/// </summary>
/// <param name="File">
/// The image itself. <c>IFormFile</c> stays in the API layer — the command carries a plain
/// <c>Stream</c> plus the original filename, so <c>RenoTrack.Application</c> never references an
/// ASP.NET Core type.
/// </param>
/// <param name="Caption">Optional caption.</param>
/// <remarks>
/// The inspection id is not here: it comes from the route. Nor is the uploading Inspector's id,
/// which comes from the JWT (D61) — this is the "who is acting" case, so it is server-derived.
/// </remarks>
public sealed record UploadInspectionPhotoRequest(IFormFile File, string? Caption);
