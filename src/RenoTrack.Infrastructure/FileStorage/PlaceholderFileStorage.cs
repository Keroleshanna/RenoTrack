using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.FileStorage;

/// <summary>
/// Placeholder only — the real disk-backed implementation (LocalDiskFileStorage) is Phase 4's
/// deliverable, not Phase 3's (ARCHITECTURE_DECISIONS.md D42). This exists solely so DI
/// composition (Slice 14) can succeed for UploadInspectionPhotoCommand before Phase 4 lands.
/// Calling it is never valid yet, so it throws loudly rather than silently pretending to store
/// anything — a silent no-op would be far worse than an obvious failure, since it would drop
/// uploaded photos without any error.
/// </summary>
public sealed class PlaceholderFileStorage : IFileStorage
{
    public Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "IFileStorage has no real implementation yet. LocalDiskFileStorage is a Phase 4 deliverable, not Phase 3's (see ARCHITECTURE_DECISIONS.md D42).");
}
