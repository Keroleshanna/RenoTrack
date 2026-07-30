using RenoTrack.Infrastructure.FileStorage;

namespace RenoTrack.Infrastructure.Tests.FileStorage;

/// <summary>
/// No database involved — this class never touches RenoTrackDbContext, so it doesn't need the
/// "Infrastructure Database" collection. Proves the placeholder cannot be accidentally used:
/// every call throws, rather than silently succeeding.
/// </summary>
public sealed class PlaceholderFileStorageTests
{
    [Fact]
    public async Task SaveAsync_AlwaysThrowsNotImplementedException()
    {
        var storage = new PlaceholderFileStorage();
        using var content = new MemoryStream();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            storage.SaveAsync(content, "inspections/1/photo.jpg", CancellationToken.None));
    }
}
