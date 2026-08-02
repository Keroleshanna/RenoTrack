using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed record SavedFile(string FileUrl, long ContentLength);

/// <summary>
/// Hand-written fake, no mocking framework (CLAUDE.md §14).
/// </summary>
/// <remarks>
/// <see cref="SavedFiles"/> models the storage's actual contents rather than a call log: a delete
/// removes the entry, so a test can assert "no file remains" the same way it would against a real
/// disk. That is what makes the compensation tests meaningful — asserting only that
/// <c>DeleteAsync</c> was called would pass even if the delete did nothing.
/// </remarks>
public sealed class FakeFileStorage : IFileStorage
{
    public List<SavedFile> SavedFiles { get; } = [];

    public List<string> DeletedFileUrls { get; } = [];

    /// <summary>Set to make <see cref="SaveAsync"/> throw, exercising the write-failure path.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>Set to make <see cref="DeleteAsync"/> throw, exercising a failed compensation.</summary>
    public Exception? DeleteFailure { get; set; }

    public Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        SavedFiles.Add(new SavedFile(fileUrl, content.Length));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken)
    {
        DeletedFileUrls.Add(fileUrl);

        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        SavedFiles.RemoveAll(file => file.FileUrl == fileUrl);
        return Task.CompletedTask;
    }
}
