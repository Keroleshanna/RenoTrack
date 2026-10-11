using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// One stored file. <see cref="Content"/> is a copy of what was written, so a test can prove which
/// bytes reached storage — the invoice archive's byte-identity guarantee depends on it (D113).
/// </summary>
public sealed record SavedFile(string FileUrl, long ContentLength, byte[] Content);

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

    /// <summary>Every key <see cref="OpenReadAsync"/> was asked for, in order.</summary>
    public List<string> OpenedFileUrls { get; } = [];

    /// <summary>Set to make <see cref="SaveAsync"/> throw, exercising the write-failure path.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>Set to make <see cref="DeleteAsync"/> throw, exercising a failed compensation.</summary>
    public Exception? DeleteFailure { get; set; }

    public async Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        var length = content.Length;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);

        SavedFiles.Add(new SavedFile(fileUrl, length, copy.ToArray()));
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

    /// <summary>
    /// Serves what <see cref="SaveAsync"/> stored, or throws <see cref="FileNotFoundException"/> as
    /// the contract requires for a missing file.
    /// </summary>
    public Task<Stream> OpenReadAsync(string fileUrl, CancellationToken cancellationToken)
    {
        OpenedFileUrls.Add(fileUrl);

        var file = SavedFiles.LastOrDefault(saved => saved.FileUrl == fileUrl)
            ?? throw new FileNotFoundException("The requested stored file does not exist.");

        return Task.FromResult<Stream>(new MemoryStream(file.Content, writable: false));
    }

    /// <summary>Places a file in storage directly, as if it had been archived earlier.</summary>
    public void Seed(string fileUrl, byte[] content) =>
        SavedFiles.Add(new SavedFile(fileUrl, content.Length, content));
}
