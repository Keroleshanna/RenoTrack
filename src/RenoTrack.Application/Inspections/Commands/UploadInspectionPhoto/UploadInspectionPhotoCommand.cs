namespace RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;

/// <summary>
/// Sequence Diagram §3 Step B. Carries the raw file content as a Stream rather than an
/// ASP.NET Core type (e.g. IFormFile) — the API layer (Phase 4) is responsible for converting
/// an incoming multipart request into this shape before dispatching the command, keeping
/// Application independent of any web-framework package.
/// </summary>
public sealed record UploadInspectionPhotoCommand(
    int InspectionId,
    Stream FileContent,
    string FileName,
    string? Caption,
    int UploadedByInspectorId);
