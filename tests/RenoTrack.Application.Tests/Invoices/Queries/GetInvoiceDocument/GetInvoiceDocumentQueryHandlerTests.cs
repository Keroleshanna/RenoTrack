using FluentValidation;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Invoices.Queries.GetInvoiceDocument;

/// <summary>
/// The Admin's download serves the archive and nothing else (Phase 14 Slice 3a, D113, BR-15): the
/// archived bytes for any issued invoice whatever its status, 409 where nothing was archived, 404 for
/// an unknown invoice, and a missing file left to fail rather than repaired.
/// </summary>
public class GetInvoiceDocumentQueryHandlerTests
{
    private const int InvoiceId = 41;
    private const string Key = "invoices/41/1b4e28ba-2fa1-11d2-883f-0016d3cca427.pdf";
    private static readonly byte[] ArchivedBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeFileStorage _fileStorage = new();
    private readonly GetInvoiceDocumentQueryHandler _handler;

    public GetInvoiceDocumentQueryHandlerTests()
    {
        _handler = new GetInvoiceDocumentQueryHandler(
            new GetInvoiceDocumentQueryValidator(),
            _invoiceRepository,
            _fileStorage);
    }

    private Invoice SeedDraft() =>
        _invoiceRepository.Seed(
            TestInvoices.AtStandardRate(projectId: 7, "RE-2026-00041", DateTime.UtcNow.AddDays(14), 1_190m),
            InvoiceId);

    private Invoice SeedIssued()
    {
        var invoice = SeedDraft();
        invoice.Send(Key);
        _fileStorage.Seed(Key, ArchivedBytes);
        return invoice;
    }

    private static async Task<byte[]> ReadAll(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    [Fact]
    public async Task AnIssuedInvoiceServesItsArchivedBytes()
    {
        SeedIssued();

        var file = await _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None);

        Assert.Equal(ArchivedBytes, await ReadAll(file.Content));
        Assert.Equal([Key], _fileStorage.OpenedFileUrls);
    }

    [Fact]
    public async Task TheFileIsNamedByTheInvoiceNumber()
    {
        SeedIssued();

        var file = await _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None);

        Assert.Equal("Rechnung-RE-2026-00041.pdf", file.FileName);
        Assert.DoesNotContain("invoices/", file.FileName);
    }

    /// <summary>
    /// The archive is the issued document whatever happened afterwards (BR-9, BR-15): a payment or a
    /// void changes the status, never what the Admin can download.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public async Task TheArchiveIsServedWhateverTheStatus(InvoiceStatus status)
    {
        var invoice = SeedIssued();
        if (status == InvoiceStatus.Paid)
            invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, recordedByAdminId: 2);
        if (status == InvoiceStatus.Void)
            invoice.Void("Issued against the wrong Project.");

        var file = await _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None);

        Assert.Equal(status, invoice.Status);
        Assert.Equal(ArchivedBytes, await ReadAll(file.Content));
    }

    /// <summary>A Draft has not been issued, so there is nothing to serve — and nothing is made up.</summary>
    [Fact]
    public async Task ADraftHasNoDocumentAndIsAConflict()
    {
        SeedDraft();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None));

        Assert.Contains("RE-2026-00041", ex.Message);
        Assert.Empty(_fileStorage.OpenedFileUrls);
    }

    [Fact]
    public async Task AnUnknownInvoiceIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None));
    }

    /// <summary>
    /// A key whose file has gone is a storage defect. The handler surfaces it and never answers it by
    /// producing a substitute — it holds no PDF generator to do so.
    /// </summary>
    [Fact]
    public async Task AMissingArchivedFileFailsAndIsNeverRegenerated()
    {
        SeedDraft().Send(Key);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _handler.HandleAsync(new GetInvoiceDocumentQuery(InvoiceId), CancellationToken.None));

        Assert.Empty(_fileStorage.SavedFiles);
    }

    /// <summary>BR-15 as a structural guarantee: the download path cannot render.</summary>
    [Fact]
    public void TheHandlerHasNoPdfGenerator()
    {
        var parameterTypes = typeof(GetInvoiceDocumentQueryHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IPdfGenerator), parameterTypes);
        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }

    /// <summary>The result names a file and carries its bytes — never the storage key.</summary>
    [Fact]
    public void TheResultCarriesNoStorageKey()
    {
        Assert.Equal(
            [nameof(InvoiceDocumentFile.Content), nameof(InvoiceDocumentFile.FileName)],
            typeof(InvoiceDocumentFile).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task ANonPositiveIdFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new GetInvoiceDocumentQuery(0), CancellationToken.None));
    }
}
