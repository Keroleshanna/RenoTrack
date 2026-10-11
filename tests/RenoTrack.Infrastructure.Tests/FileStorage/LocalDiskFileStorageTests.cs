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

    // ---- A failed write leaves nothing behind (Phase 14 Slice 3a, D113) --------------------

    /// <summary>
    /// A write that fails part-way must not leave a truncated file that looks like a stored one —
    /// for an invoice, a half-written PDF where the archive should be.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RemovesAPartiallyWrittenFileWhenTheWriteFails()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<IOException>(
            () => storage.SaveAsync(new FailingAfterFirstReadStream(), "invoices/3/partial.pdf", CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_root, "invoices", "3", "partial.pdf")));
    }

    /// <summary>
    /// The partial-write cleanup removes only what this call created: when CreateNew refuses because
    /// the file already exists, that existing file — possibly an issued invoice — is untouched.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NeverRemovesAnExistingFileWhenRefusingToOverwriteIt()
    {
        var storage = CreateStorage();
        await storage.SaveAsync(Content("issued"), "invoices/4/archived.pdf", CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(
            () => storage.SaveAsync(Content("other"), "invoices/4/archived.pdf", CancellationToken.None));

        Assert.Equal("issued", await File.ReadAllTextAsync(Path.Combine(_root, "invoices", "4", "archived.pdf")));
    }

    // ---- OpenReadAsync (Phase 14 Slice 3a, D113) --------------------------------------------

    [Fact]
    public async Task OpenReadAsync_ReturnsExactlyTheBytesThatWereSaved()
    {
        var storage = CreateStorage();
        byte[] pdf = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0x10, 0x0A];
        await storage.SaveAsync(new MemoryStream(pdf), "invoices/5/doc.pdf", CancellationToken.None);

        await using var read = await storage.OpenReadAsync("invoices/5/doc.pdf", CancellationToken.None);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);

        Assert.Equal(pdf, buffer.ToArray());
    }

    /// <summary>
    /// Reading is not writing: an open read leaves the archived file exactly as it was, and two
    /// readers can hold it at once (an Admin download alongside an email retry).
    /// </summary>
    [Fact]
    public async Task OpenReadAsync_AllowsConcurrentReadersAndChangesNothing()
    {
        var storage = CreateStorage();
        await storage.SaveAsync(Content("issued"), "invoices/6/doc.pdf", CancellationToken.None);

        await using (var first = await storage.OpenReadAsync("invoices/6/doc.pdf", CancellationToken.None))
        await using (var second = await storage.OpenReadAsync("invoices/6/doc.pdf", CancellationToken.None))
        {
            Assert.False(first.CanWrite);
            Assert.False(second.CanWrite);
        }

        Assert.Equal("issued", await File.ReadAllTextAsync(Path.Combine(_root, "invoices", "6", "doc.pdf")));
    }

    /// <summary>
    /// A missing file is a defect to report, and the report must not carry the key or the server's
    /// directory layout — it reaches the error log and a notification's failure record. Nothing is
    /// chained either, because the BCL exception's message names the absolute path.
    /// </summary>
    [Theory]
    [InlineData("invoices/7/missing.pdf")]
    [InlineData("invoices/never-created-directory/missing.pdf")]
    public async Task OpenReadAsync_ReportsAMissingFileWithoutItsKeyOrPath(string fileUrl)
    {
        var storage = CreateStorage();
        Directory.CreateDirectory(Path.Combine(_root, "invoices", "7"));

        var thrown = await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.OpenReadAsync(fileUrl, CancellationToken.None));

        Assert.DoesNotContain("missing.pdf", thrown.ToString());
        Assert.DoesNotContain("invoices", thrown.ToString());
        Assert.DoesNotContain(_root, thrown.ToString());
        Assert.Null(thrown.InnerException);
        Assert.Null(thrown.FileName);
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("invoices/../../escape.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task OpenReadAsync_RejectsKeysResolvingOutsideTheRoot(string fileUrl)
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(fileUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenReadAsync_RejectsAnEmptyKey(string fileUrl)
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(fileUrl, CancellationToken.None));
    }

    /// <summary>Yields a few bytes, then fails — a disk filling up mid-write.</summary>
    private sealed class FailingAfterFirstReadStream : Stream
    {
        private bool _served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
                throw new IOException("The device is full.");

            _served = true;
            buffer[offset] = 0x25;
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
                throw new IOException("The device is full.");

            _served = true;
            buffer.Span[0] = 0x25;
            return ValueTask.FromResult(1);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
