using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.SendInvoice;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Invoices.Commands.SendInvoice;

/// <summary>
/// Orchestration and ordering. The <c>Draft → Sent</c> transition and its guards are the aggregate's
/// own and are proved exhaustively in <c>InvoiceTests</c>; what these prove is that the handler
/// archives exactly one document before it commits, commits the Invoice and its token together,
/// removes the document when that commit fails, attaches the very bytes it archived, audits and
/// emails only after the commit, and leaves no trace at all when anything earlier refuses
/// (Phase 14 Slice 3a, D113).
/// </summary>
public class SendInvoiceCommandHandlerTests
{
    private const int AdminId = 2;
    private const int InvoiceId = 55;
    private const int ProjectId = 77;
    private const string CustomerAddress = "Musterstraße 1\n12345 Musterstadt";

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeProjectRepository _projectRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeTokenLinkRepository _tokenLinkRepository = new();
    private readonly FakeTokenLinkService _tokenLinkService = new();
    private readonly FakePdfGenerator _pdfGenerator = new();
    private readonly FakeFileStorage _fileStorage = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeAuditService _auditService = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly CapturingLogger<SendInvoiceCommandHandler> _logger = new();
    private readonly SendInvoiceCommandHandler _handler;

    public SendInvoiceCommandHandlerTests()
    {
        _handler = new SendInvoiceCommandHandler(
            new SendInvoiceCommandValidator(),
            _invoiceRepository,
            _projectRepository,
            _customerRepository,
            _tokenLinkRepository,
            _tokenLinkService,
            _pdfGenerator,
            _fileStorage,
            InvoiceCalendar.ForEuropeBerlin(),
            _unitOfWork,
            _auditService,
            _emailSender,
            _logger);
    }

    private Invoice SeedDraftInvoice(decimal gross = 8_000.00m, string? customerAddress = CustomerAddress)
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567", customerAddress));

        _projectRepository.Seed(
            Project.Create(customer.Id, angebotId: 3, Money.FromExact(25_673.36m)), ProjectId);

        return _invoiceRepository.Seed(
            TestInvoices.AtStandardRate(
                ProjectId, "RE-2026-00017", DateTime.UtcNow.AddDays(14), gross),
            InvoiceId);
    }

    private static SendInvoiceCommand Command => new(InvoiceId, AdminId);

    private void AssertNothingWasIssued()
    {
        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Empty(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    // ---- Happy path -----------------------------------------------------

    [Fact]
    public async Task ADraftInvoiceMovesToSent()
    {
        var invoice = SeedDraftInvoice();

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(InvoiceStatus.Sent, result.Status);
    }

    [Fact]
    public async Task ATokenLinkIsIssuedForTheInvoice()
    {
        var invoice = SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var tokenLink = Assert.Single(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(TokenLinkEntityType.Invoice, tokenLink.EntityType);
        Assert.Equal(invoice.Id, tokenLink.EntityId);
        Assert.Null(tokenLink.UsedAt);
    }

    /// <summary>
    /// The status change and the token row must reach the database together: a committed link for an
    /// Invoice that never became <c>Sent</c> is a live credential for a bill nobody issued, and a
    /// <c>Sent</c> Invoice with no link is a customer who cannot see what they owe.
    /// </summary>
    [Fact]
    public async Task BothWritesShareOneSaveChanges()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task NoExplicitTransactionIsOpened()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(0, _unitOfWork.BeginTransactionCallCount);
    }

    [Fact]
    public async Task TheSendIsAuditedAgainstTheInvoice()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var entry = Assert.Single(_auditService.Calls);
        Assert.Equal(nameof(Invoice), entry.EntityType);
        Assert.Equal(InvoiceId, entry.EntityId);
        Assert.Equal(AuditAction.InvoiceSent, entry.Action);
        Assert.Equal(AdminId, entry.PerformedByUserId);
    }

    /// <summary>
    /// Sequence Diagram §9 addresses the mail to <c>Customer.Email</c>, reached through
    /// <c>Invoice → Project → Customer</c>. The notification carries the raw token, never a URL —
    /// the base address is deployment configuration Application deliberately cannot see.
    /// </summary>
    [Fact]
    public async Task TheCustomerIsEmailedTheTokenLink()
    {
        var invoice = SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var notification = Assert.Single(_emailSender.InvoiceReadyNotifications);
        Assert.Equal("m.klein@example.com", notification.RecipientEmail);
        Assert.Equal("M. Klein", notification.RecipientName);
        Assert.Equal(invoice.InvoiceNumber, notification.InvoiceNumber);
        Assert.Equal(invoice.GrossAmount.Amount, notification.GrossAmount);
        Assert.Equal(_tokenLinkRepository.AddedTokenLinks.Single().Token, notification.Token);
    }

    // ---- The archived document (D113, BR-15) ----------------------------

    [Fact]
    public async Task TheDocumentIsArchivedOnceUnderAnInvoiceScopedKey()
    {
        var invoice = SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var saved = Assert.Single(_fileStorage.SavedFiles);
        Assert.Matches(
            new Regex($"^invoices/{InvoiceId}/[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}\\.pdf$"),
            saved.FileUrl);
        Assert.Equal(saved.FileUrl, invoice.DocumentFileUrl);
    }

    /// <summary>The key never names the invoice number or anything else a person could read.</summary>
    [Fact]
    public async Task TheKeyNamesNeitherTheInvoiceNumberNorTheCustomer()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var key = Assert.Single(_fileStorage.SavedFiles).FileUrl;
        Assert.DoesNotContain("RE-2026", key);
        Assert.DoesNotContain("Klein", key);
    }

    [Fact]
    public async Task TheDocumentIsRenderedExactlyOnce()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Single(_pdfGenerator.RenderedInvoices);
    }

    /// <summary>
    /// The archive freezes the customer as they stood at the moment of sending (D112's consequence):
    /// the rendered document prints the Customer's current name and address.
    /// </summary>
    [Fact]
    public async Task TheDocumentPrintsTheCustomerAsAtSend()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var document = Assert.Single(_pdfGenerator.RenderedInvoices);
        Assert.Equal("M. Klein", document.Customer.Name);
        Assert.Equal(CustomerAddress, document.Customer.Address);
        Assert.Equal("RE-2026-00017", document.InvoiceNumber);
    }

    /// <summary>
    /// Write first, commit second (D113): a committed row pointing at a file that was never written
    /// would be an issued invoice with no document. Observed at the moment of the commit itself.
    /// </summary>
    [Fact]
    public async Task TheDocumentIsWrittenBeforeTheCommit()
    {
        SeedDraftInvoice();
        var filesAtCommit = -1;
        _unitOfWork.OnSaveChanges = () => filesAtCommit = _fileStorage.SavedFiles.Count;

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Equal(1, filesAtCommit);
    }

    /// <summary>
    /// The attachment is the archive by construction: the exact bytes written to storage, and no
    /// second rendering — the fake gives each rendering distinct content, as a real PDF's embedded
    /// creation time would.
    /// </summary>
    [Fact]
    public async Task TheEmailAttachesExactlyTheBytesWrittenToTheArchive()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        var archived = Assert.Single(_fileStorage.SavedFiles).Content;
        var notification = Assert.Single(_emailSender.InvoiceReadyNotifications);
        Assert.NotNull(notification.InvoicePdf);
        Assert.Equal(archived, notification.InvoicePdf);
        Assert.Single(_pdfGenerator.RenderedInvoices);
    }

    [Fact]
    public async Task TheResultReportsThatADocumentExistsWithoutExposingItsKey()
    {
        SeedDraftInvoice();

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.True(result.HasDocument);
        Assert.DoesNotContain(
            typeof(RenoTrack.Application.Invoices.Dtos.InvoiceDto).GetProperties(),
            property => property.Name.Contains("FileUrl", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Guards ---------------------------------------------------------

    [Fact]
    public async Task AnUnknownInvoiceIsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>StateMachine.md §3.3: only a <c>Draft</c> Invoice may be sent.</summary>
    [Fact]
    public async Task AnAlreadySentInvoiceIsRejected()
    {
        SeedDraftInvoice().Send("invoices/1/archived.pdf");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    /// <summary>StateMachine.md §3.3's guard: "Invoice has a valid GrossAmount &gt; 0".</summary>
    [Fact]
    public async Task AZeroGrossInvoiceCannotBeSent()
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567", CustomerAddress));
        _projectRepository.Seed(Project.Create(customer.Id, 3, Money.Zero), ProjectId);
        _invoiceRepository.Seed(
            TestInvoices.AtStandardRate(ProjectId, "RE-2026-00018", DateTime.UtcNow, 0m),
            InvoiceId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));
    }

    [Fact]
    public async Task ANonPositiveIdFailsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _handler.HandleAsync(new SendInvoiceCommand(0, AdminId), CancellationToken.None));
    }

    // ---- No residue on rejection ----------------------------------------

    /// <summary>
    /// Architecture §9's ordering principle: the Domain guard runs before anything is rendered,
    /// written or generated, so a refused send archives nothing, issues no credential, commits
    /// nothing, audits nothing and emails nothing.
    /// </summary>
    [Fact]
    public async Task ARefusedSendLeavesNoTrace()
    {
        SeedDraftInvoice().Send("invoices/1/archived.pdf");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Empty(_pdfGenerator.RenderedInvoices);
        Assert.Empty(_tokenLinkRepository.AddedTokenLinks);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    [Fact]
    public async Task AZeroGrossRejectionRendersAndIssuesNothing()
    {
        var customer = _customerRepository.Seed(
            Customer.Create(leadId: 1, "M. Klein", "m.klein@example.com", "0176 1234567", CustomerAddress));
        _projectRepository.Seed(Project.Create(customer.Id, 3, Money.Zero), ProjectId);
        _invoiceRepository.Seed(
            TestInvoices.AtStandardRate(ProjectId, "RE-2026-00018", DateTime.UtcNow, 0m),
            InvoiceId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Empty(_pdfGenerator.RenderedInvoices);
        AssertNothingWasIssued();
    }

    /// <summary>
    /// Missing business data is the Admin's to supply, so the document assembler's refusal stays an
    /// <see cref="InvalidOperationException"/> — a 409 (D111, D113) — and it happens before anything
    /// is rendered or written.
    /// </summary>
    [Fact]
    public async Task ACustomerWithoutAnAddressIsRefusedBeforeAnythingIsRendered()
    {
        SeedDraftInvoice(customerAddress: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Contains("the customer's address", ex.Message);
        Assert.Empty(_pdfGenerator.RenderedInvoices);
        AssertNothingWasIssued();
    }

    /// <summary>
    /// A configuration or renderer fault is the generator's to classify (as a 500, D113); the handler
    /// neither catches nor re-wraps it, and nothing has been written yet.
    /// </summary>
    [Fact]
    public async Task ARenderFailurePropagatesUnchangedAndWritesNothing()
    {
        SeedDraftInvoice();
        var fault = new RenderFaultForTest();
        _pdfGenerator.RenderFailure = fault;

        var thrown = await Assert.ThrowsAsync<RenderFaultForTest>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Same(fault, thrown);
        AssertNothingWasIssued();
    }

    /// <summary>A failed write happens before the commit, so the Invoice is never committed as Sent.</summary>
    [Fact]
    public async Task AStorageWriteFailurePropagatesAndCommitsNothing()
    {
        SeedDraftInvoice();
        var fault = new IOException("Disk full.");
        _fileStorage.SaveFailure = fault;

        var thrown = await Assert.ThrowsAsync<IOException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Same(fault, thrown);
        AssertNothingWasIssued();
        Assert.Empty(_fileStorage.DeletedFileUrls);
    }

    // ---- A failed commit removes the document it was going to point at ---

    /// <summary>
    /// A commit failure other than a lost race is ambiguous: a connection lost after SQL Server
    /// committed throws all the same, so the row may point at this file. Deleting it could destroy an
    /// issued invoice (BR-15), so the file stays — an orphan at worst (D113) — and the original
    /// exception is still the caller's answer.
    /// </summary>
    [Fact]
    public async Task AnAmbiguousCommitFailureKeepsTheArchivedDocumentAndRethrowsTheOriginalFailure()
    {
        var invoice = SeedDraftInvoice();
        var fault = new InvalidOperationException("The database is unavailable.");
        _unitOfWork.SaveFailure = fault;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Same(fault, thrown);
        Assert.Equal(invoice.DocumentFileUrl, Assert.Single(_fileStorage.SavedFiles).FileUrl);
        Assert.Empty(_fileStorage.DeletedFileUrls);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    /// <summary>
    /// Once the document is written, the commit runs with <see cref="CancellationToken.None"/>: a
    /// client disconnecting mid-commit must not turn a commit that may already have happened into a
    /// cancellation the handler cannot interpret.
    /// </summary>
    [Fact]
    public async Task TheCommitCannotBeCancelledByTheRequest()
    {
        SeedDraftInvoice();
        using var request = new CancellationTokenSource();

        await _handler.HandleAsync(Command, request.Token);

        Assert.False(_unitOfWork.LastSaveCancellationToken.CanBeCanceled);
    }

    /// <summary>
    /// A lost race on <c>Invoices.Status</c> reaches the handler as <see cref="ConflictException"/>
    /// (UnitOfWork's D96 translation) — a 409, and the one failure whose rollback is provable. The
    /// losing send's document is removed, so a race can never leave two archived documents behind
    /// (D113), and the original exception is rethrown.
    /// </summary>
    [Fact]
    public async Task ALostConcurrencyRaceIsAConflictAndRemovesTheLosersDocument()
    {
        var invoice = SeedDraftInvoice();
        var conflict = new ConflictException("The record was changed by another request.");
        _unitOfWork.SaveFailure = conflict;

        var thrown = await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Same(conflict, thrown);
        Assert.Equal([invoice.DocumentFileUrl!], _fileStorage.DeletedFileUrls);
        Assert.Empty(_fileStorage.SavedFiles);
        Assert.Empty(_auditService.Calls);
        Assert.Empty(_emailSender.InvoiceReadyNotifications);
    }

    /// <summary>
    /// The cleanup removes the file this request wrote — the key the Invoice was given — and nothing
    /// else.
    /// </summary>
    [Fact]
    public async Task TheCleanupRemovesOnlyTheDocumentThisRequestWrote()
    {
        var invoice = SeedDraftInvoice();
        _fileStorage.Seed("invoices/99/someone-else.pdf", [1, 2, 3]);
        _unitOfWork.SaveFailure = new ConflictException("The record was changed by another request.");

        await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Equal([invoice.DocumentFileUrl!], _fileStorage.DeletedFileUrls);
        Assert.Equal("invoices/99/someone-else.pdf", Assert.Single(_fileStorage.SavedFiles).FileUrl);
    }

    /// <summary>
    /// A failed cleanup must not replace the commit's own exception, and its log names the Invoice —
    /// never the storage key (D113).
    /// </summary>
    [Fact]
    public async Task AFailedCleanupIsLoggedByInvoiceIdNotByKeyAndTheCommitFailureStillPropagates()
    {
        var invoice = SeedDraftInvoice();
        var commitFailure = new ConflictException("The record was changed by another request.");
        _unitOfWork.SaveFailure = commitFailure;
        _fileStorage.DeleteFailure = new IOException("Access denied.");

        var thrown = await Assert.ThrowsAsync<ConflictException>(
            () => _handler.HandleAsync(Command, CancellationToken.None));

        Assert.Same(commitFailure, thrown);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(InvoiceId.ToString(), entry.Message);
        Assert.DoesNotContain(invoice.DocumentFileUrl!, entry.Message);
        Assert.DoesNotContain("invoices/", entry.Message);
    }

    [Fact]
    public async Task ASuccessfulSendLogsNothing()
    {
        SeedDraftInvoice();

        await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.Empty(_logger.Entries);
    }

    // ---- Structure -------------------------------------------------------

    /// <summary>
    /// `PermissionMatrix.md` §5 marks "Send Invoice" Admin <c>F</c>, so no ownership rule exists to
    /// enforce (CLAUDE.md §16).
    /// </summary>
    [Fact]
    public void TheHandlerTakesNoOwnershipValidator()
    {
        var parameterTypes = typeof(SendInvoiceCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IOwnershipValidator), parameterTypes);
    }

    private sealed class RenderFaultForTest : Exception;
}
