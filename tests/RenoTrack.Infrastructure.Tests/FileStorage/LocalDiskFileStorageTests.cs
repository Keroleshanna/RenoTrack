using System.Text;
using RenoTrack.Infrastructure.FileStorage;

namespace RenoTrack.Infrastructure.Tests.FileStorage;

/// <summary>
/// Real disk I/O in a per-test temporary root — no database, and no fake filesystem, since the
/// behaviours worth pinning here (path resolution, refusal to overwrite, idempotent delete) are
/// exactly the ones an abstraction over the filesystem would have to reimplement.
/// </summary>
/// <remarks>
/// The containment tests matter even though today's only caller cannot produce an unsafe key: the
/// <c>IFileStorage.SaveAsync</c> contract takes a plain string, so this class must be safe against
/// any caller rather than trusting the one that exists now.
/// </remarks>
public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RenoTrackStorageTests", Guid.NewGuid().ToString("N"));

    private LocalDiskFileStorage CreateStorage() => new(new FileStorageOptions { RootPath = _root });

    private static Stream Content(string text = "photo-bytes") => new MemoryStream(Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WritesTheContentUnderTheRoot()
    {
        var storage = CreateStorage();

        await storage.SaveAsync(Content(), "inspections/42/photo.jpg", CancellationToken.None);

        var expected = Path.Combine(_root, "inspections", "42", "photo.jpg");
        Assert.True(File.Exists(expected));
        Assert.Equal("photo-bytes", await File.ReadAllTextAsync(expected));
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingDirectories()
    {
        var storage = CreateStorage();

        await storage.SaveAsync(Content(), "inspections/7/nested/photo.jpg", CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_root, "inspections", "7", "nested")));
    }

    [Fact]
    public async Task SaveAsync_RefusesToOverwriteAnExistingFile()
    {
        var storage = CreateStorage();
        await storage.SaveAsync(Content("original"), "inspections/1/photo.jpg", CancellationToken.None);

        // A collision means a GUID clash or a bug. Silently replacing would destroy evidence
        // attached to an Inspection, so failing loudly is the correct response.
        await Assert.ThrowsAsync<IOException>(
            () => storage.SaveAsync(Content("replacement"), "inspections/1/photo.jpg", CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "inspections", "1", "photo.jpg")));
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("../../escape.jpg")]
    [InlineData("..\\escape.jpg")]
    [InlineData("inspections/../../escape.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\evil.exe")]
    public async Task SaveAsync_RejectsKeysResolvingOutsideTheRoot(string fileUrl)
    {
        var storage = CreateStorage();

        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(Content(), fileUrl, CancellationToken.None));

        // The message must not echo the attempted key back to the caller.
        Assert.DoesNotContain("escape", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwd", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_RejectsAnEscapingKeyWithoutWritingAnythingAnywhere()
    {
        var storage = CreateStorage();
        Directory.CreateDirectory(_root);

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(Content(), "../escaped.jpg", CancellationToken.None));

        // Nothing inside the root, and nothing in its parent either — a rejection must not be a
        // rejection-after-writing.
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(Directory.GetParent(_root)!.FullName, "escaped.jpg")));
    }

    [Fact]
    public async Task SaveAsync_RejectsASiblingDirectoryThatMerelySharesTheRootsPrefix()
    {
        var storage = CreateStorage();

        // "<root>-sibling" starts with the root's own string, so a naive StartsWith(root) check
        // without the separator would let this through.
        var siblingKey = "../" + Path.GetFileName(_root) + "-sibling/photo.jpg";

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(Content(), siblingKey, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_RejectsAnEmptyKey()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(Content(), "   ", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_RejectsAKeyContainingCharactersThePlatformCannotStore()
    {
        var storage = CreateStorage();

        // A NUL makes Path.Combine throw; rephrased into a clean ArgumentException rather than
        // propagating a framework message that repeats the key.
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => storage.SaveAsync(Content(), "inspections/1/photo" + (char)0 + ".jpg", CancellationToken.None));

        Assert.Contains("valid storage path", thrown.Message);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnExistingFile()
    {
        var storage = CreateStorage();
        await storage.SaveAsync(Content(), "inspections/9/photo.jpg", CancellationToken.None);

        await storage.DeleteAsync("inspections/9/photo.jpg", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, "inspections", "9", "photo.jpg")));
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotentForAFileThatIsNotThere()
    {
        var storage = CreateStorage();

        // Its only caller is a compensation path running after another failure, where "never
        // written" and "already gone" are both fine and neither should raise a second exception.
        await storage.DeleteAsync("inspections/9/never-existed.jpg", CancellationToken.None);
    }

    [Fact]
    public async Task DeleteAsync_RejectsKeysResolvingOutsideTheRoot()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.DeleteAsync("../../something.jpg", CancellationToken.None));
    }
}
