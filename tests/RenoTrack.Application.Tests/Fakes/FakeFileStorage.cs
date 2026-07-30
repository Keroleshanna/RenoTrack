using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed record SavedFile(string FileUrl, long ContentLength);

public sealed class FakeFileStorage : IFileStorage
{
    public List<SavedFile> SavedFiles { get; } = [];

    public Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken)
    {
        SavedFiles.Add(new SavedFile(fileUrl, content.Length));
        return Task.CompletedTask;
    }
}
