using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceDocumentByToken;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Invoices.Queries.GetPublicInvoiceDocumentByToken;

/// <summary>
/// The customer's download serves the archive and nothing else (Phase 14 Slice 3b, D114, BR-15).
/// These cover the read's token checks applied to the download, the archived bytes for every status,
/// the 409 where nothing was archived, a link that is never consumed, and a structure that cannot
/// render.
/// </summary>
public class GetPublicInvoiceDocumentByTokenQueryHandlerTests
{
    private const int InvoiceId = 63;
    private const string Key = "invoices/63/0f8fad5b-d9cb-469f-a165-70867728950e.pdf";
    private static readonly byte[] ArchivedBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A];

    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeFileStorage _fileStorage = new();
    private readonly GetPublicInvoiceDocumentByTokenQueryHandler _handler;

    public GetPublicInvoiceDocumentByTokenQueryHandlerTests()
    {
        _handler = new GetPublicInvoiceDocumentByTokenQueryHandler(
            new GetPublicInvoiceDocumentByTokenQueryValidator(),
            _tokenLinkRepository,
            _invoiceRepository,
            _fileStorage);
    }

    private Invoice SeedDraft() =>
        _invoiceRepository.Seed(
            TestInvoices.AtStandardRate(projectId: 9, "RE-2026-00063", DateTime.UtcNow.AddDays(14), 2_380m),
            InvoiceId);

    private Invoice SeedIssued()
    {
        var invoice = SeedDraft();
        invoice.Send(Key);
        _fileStorage.Seed(Key, ArchivedBytes);
        return invoice;
    }

    private async Task<TokenLink> SeedTokenAsync(
        TokenLinkEntityType entityType = TokenLinkEntityType.Invoice,
        int? entityId = null,
        DateTime? expiresAt = null)
    {
        var link = TokenLink.Create(
            entityType,
            entityId ?? InvoiceId,
            $"token-{Guid.NewGuid():N}",
            expiresAt ?? DateTime.UtcNow.AddDays(30));

        await _tokenLinkRepository.AddAsync(link, CancellationToken.None);
        return link;
    }

    private Task<InvoiceDocumentFile> Download(string token) =>
        _handler.HandleAsync(new GetPublicInvoiceDocumentByTokenQuery(token), CancellationToken.None);

    private static async Task<byte[]> ReadAll(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    // ---- The archive -----------------------------------------------------

    [Fact]
    public async Task AValidTokenServesTheArchivedBytes()
    {
        SeedIssued();
        var link = await SeedTokenAsync();

        var file = await Download(link.Token);

        Assert.Equal(ArchivedBytes, await ReadAll(file.Content));
        Assert.Equal([Key], _fileStorage.OpenedFileUrls);
    }

    [Fact]
    public async Task TheFileIsNamedByTheInvoiceNumberAndNeverByTheKey()
    {
        SeedIssued();
        var link = await SeedTokenAsync();

        var file = await Download(link.Token);

        Assert.Equal("Rechnung-RE-2026-00063.pdf", file.FileName);
        Assert.DoesNotContain("invoices/", file.FileName);
    }

    /// <summary>
    /// The Product Owner's decision: a voided invoice's document stays downloadable, because the
    /// archive is the issued document (D113, BR-15). A payment does not change it either.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task TheArchiveIsServedWhateverTheStatus(InvoiceStatus status)
    {
        var invoice = SeedIssued();
        if (status == InvoiceStatus.Overdue)
            invoice.MarkOverdue(invoice.DueDate.AddDays(1));
        if (status == InvoiceStatus.Paid)
            invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, recordedByAdminId: 2);
        if (status == InvoiceStatus.Void)
            invoice.Void("Issued against the wrong Project.");
        var link = await SeedTokenAsync();

        var file = await Download(link.Token);

        Assert.Equal(status, invoice.Status);
        Assert.Equal(ArchivedBytes, await ReadAll(file.Content));
    }

    // ---- The link is read, never consumed --------------------------------

    /// <summary>
    /// A download is a read. PermissionMatrix.md §7 grants no Invoice decision action, so the link
    /// is never marked used, however often the customer downloads.
    /// </summary>
    [Fact]
    public async Task DownloadingNeverConsumesTheLink()
    {
        SeedIssued();
        var link = await SeedTokenAsync();

        await ReadAll((await Download(link.Token)).Content);
        await ReadAll((await Download(link.Token)).Content);

        Assert.Null(link.UsedAt);
    }

    /// <summary>An already-used link still downloads: prior use is not checked on a read (§12).</summary>
    [Fact]
    public async Task AUsedLinkStillDownloads()
    {
        SeedIssued();
        var link = await SeedTokenAsync();
        link.MarkUsed();

        var file = await Download(link.Token);

        Assert.Equal(ArchivedBytes, await ReadAll(file.Content));
    }

    // ---- Sequence Diagram §12 --------------------------------------------

    [Fact]
    public async Task AnUnknownTokenIsNotFoundWithTheReadsOwnText()
    {
        SeedIssued();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Download("nope"));

        Assert.Equal(GetPublicInvoiceByTokenQueryHandler.NotValidMessage, ex.Message);
        Assert.Empty(_fileStorage.OpenedFileUrls);
    }

    [Fact]
    public async Task AnAngebotTokenIsIndistinguishableFromAnUnknownOne()
    {
        SeedIssued();
        var angebotLink = await SeedTokenAsync(TokenLinkEntityType.Angebot);

        var wrongType = await Assert.ThrowsAsync<NotFoundException>(() => Download(angebotLink.Token));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => Download("nope"));

        Assert.Equal(unknown.Message, wrongType.Message);
        Assert.Empty(_fileStorage.OpenedFileUrls);
    }

    [Fact]
    public async Task AnExpiredTokenIsGoneAndNothingIsRead()
    {
        SeedIssued();

        // TokenLink.Create refuses a link born expired, so the window is shortened, then passed.
        var link = await SeedTokenAsync(expiresAt: DateTime.UtcNow.AddMilliseconds(50));
        Thread.Sleep(120);

        await Assert.ThrowsAsync<GoneException>(() => Download(link.Token));
        Assert.Empty(_fileStorage.OpenedFileUrls);
    }

    /// <summary>
    /// A token row pointing at no Invoice (TokenLinks has no foreign key) is a 404 whose message
    /// names no internal id. A mapped exception's message is an anonymous caller's ProblemDetails
    /// detail (D59).
    /// </summary>
    [Fact]
    public async Task ATokenPointingAtNoInvoiceIsNotFoundWithoutAnId()
    {
        var link = await SeedTokenAsync(entityId: 987_654);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Download(link.Token));

        Assert.Equal(GetPublicInvoiceByTokenQueryHandler.NotValidMessage, ex.Message);
        Assert.DoesNotContain("987654", ex.Message);
    }

    [Fact]
    public async Task AnEmptyTokenFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Download(""));
    }

    // ---- No archive, missing file ------------------------------------------

    /// <summary>
    /// An Invoice with no archived document, which in production is one sent before Slice 3a. Here
    /// it is a Draft carrying a token. That is not reachable in production, but it is the honest way
    /// to obtain a null key without reflecting past the aggregate. The handler checks the key, not
    /// the status, so the branch exercised is the same. The message names nothing.
    /// </summary>
    [Fact]
    public async Task AnInvoiceWithoutAnArchiveIsAConflictThatNamesNothing()
    {
        SeedDraft();
        var link = await SeedTokenAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Download(link.Token));

        Assert.Equal(GetPublicInvoiceDocumentByTokenQueryHandler.NoDocumentMessage, ex.Message);
        Assert.DoesNotContain("RE-2026", ex.Message);
        Assert.DoesNotContain(InvoiceId.ToString(), ex.Message);
        Assert.DoesNotContain(link.Token, ex.Message);
        Assert.Empty(_fileStorage.OpenedFileUrls);
    }

    /// <summary>
    /// A key whose file has gone is a storage defect: it fails, and nothing is produced in its
    /// place. The handler has no PDF generator to produce anything with.
    /// </summary>
    [Fact]
    public async Task AMissingArchivedFileFailsAndIsNeverRegenerated()
    {
        SeedDraft().Send(Key);
        var link = await SeedTokenAsync();

        await Assert.ThrowsAsync<FileNotFoundException>(() => Download(link.Token));

        Assert.Empty(_fileStorage.SavedFiles);
    }

    // ---- Structure -----------------------------------------------------------

    /// <summary>
    /// BR-15 as a structural guarantee: the download path cannot render. It also cannot write: no
    /// unit of work, so it can never consume the link either.
    /// </summary>
    [Fact]
    public void TheHandlerCanNeitherRenderNorWrite()
    {
        var parameterTypes = typeof(GetPublicInvoiceDocumentByTokenQueryHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IPdfGenerator), parameterTypes);
        Assert.DoesNotContain(typeof(IUnitOfWork), parameterTypes);
        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
        Assert.Contains(typeof(IFileStorage), parameterTypes);
    }

    [Fact]
    public void TheHandlerAnswersWithTheSameFileTypeAsTheAdminDownload()
    {
        Assert.True(typeof(IQueryHandler<GetPublicInvoiceDocumentByTokenQuery, InvoiceDocumentFile>)
            .IsAssignableFrom(typeof(GetPublicInvoiceDocumentByTokenQueryHandler)));
    }
}
