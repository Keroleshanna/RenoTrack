using FluentValidation;
using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Commands.SendInvoice;

/// <summary>
/// SRS FR-8.3 / Sequence Diagram §9. The moment an Invoice becomes a customer-facing document: the
/// Invoice moves <c>Draft → Sent</c>, its PDF is rendered and archived, a token link is issued, and
/// the customer is emailed the link with the archived PDF attached.
///
/// <para>
/// <b>No <c>IOwnershipValidator</c>, deliberately.</b> `PermissionMatrix.md` §5 marks "Send Invoice"
/// Admin <c>F</c> — the same reasoning that keeps one out of <c>SendAngebotCommandHandler</c>
/// (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>Sending is issuing, and the archived PDF is the issued invoice (Phase 14 Slice 3a, D113,
/// BR-15).</b> It is rendered exactly once, here, and every later download and email retry serves
/// those bytes. It freezes everything printed on it as it stood at this moment — the customer's name
/// and address (D112) and the company's legal identity from configuration — which is why it is
/// never regenerated afterwards.
/// </para>
/// <para>
/// <b>The order is the design, and each step's position is deliberate:</b>
/// </para>
/// <list type="number">
///   <item>
///     The storage key is computed before any I/O and handed to <c>Invoice.Send</c>, whose guards
///     (<c>Draft</c>, gross &gt; 0) therefore run first (Architecture §9, CLAUDE.md §12). A refused
///     send renders nothing and writes nothing.
///   </item>
///   <item>
///     The document is assembled, which refuses an invoice missing a customer address, a description
///     or its VAT lines — business data an Admin can supply, so it surfaces as 409. Then it is
///     rendered; a missing company identity or a renderer fault is a server defect and surfaces as
///     500 from the generator (D113). Neither writes anything.
///   </item>
///   <item>
///     The PDF is written to storage <em>before</em> the commit. Writing a file and committing a row
///     can never be atomic, so the order is chosen: a row pointing at a file that was never written
///     would be an issued invoice with no document, while a file with no row is inert.
///   </item>
///   <item>
///     <b>The Invoice and the token link commit in one <c>SaveChangesAsync</c></b> — EF Core's
///     implicit transaction covers both, so no explicit transaction is opened. The commit runs with
///     <see cref="CancellationToken.None"/>, so a client disconnect cannot cancel it in flight. The
///     file just written is removed best-effort <b>only</b> after a lost concurrency race on
///     <c>Invoices.Status</c> — the <see cref="ConflictException"/> (a 409) <c>UnitOfWork</c> reports,
///     whose rollback is provable. Any other commit failure is ambiguous (the row may have
///     committed), so the file stays: an orphan is accepted, a deleted issued invoice is not. Either
///     way the original exception is rethrown. That is compensation, not atomicity: a crash between
///     the write and the commit still leaves an orphaned file, an accepted and documented window.
///   </item>
///   <item>Audit and email follow the commit, never precede it (D50, CLAUDE.md §11).</item>
/// </list>
/// <para>
/// <b>The email attaches the very array that was written.</b> Not a second rendering — a PDF embeds
/// its creation time, so a re-render would be a different file.
/// </para>
/// </summary>
public sealed class SendInvoiceCommandHandler(
    IValidator<SendInvoiceCommand> validator,
    IInvoiceRepository invoiceRepository,
    IProjectRepository projectRepository,
    ICustomerRepository customerRepository,
    ITokenLinkRepository tokenLinkRepository,
    ITokenLinkService tokenLinkService,
    IPdfGenerator pdfGenerator,
    IFileStorage fileStorage,
    InvoiceCalendar invoiceCalendar,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender,
    ILogger<SendInvoiceCommandHandler> logger) : ICommandHandler<SendInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(SendInvoiceCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var invoice = await invoiceRepository.GetByIdAsync(command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), command.InvoiceId);

        // The recipient is reached through Project → Customer, which is how Sequence Diagram §9
        // addresses the mail (`to=Customer.Email`), and the same Customer is the one the document
        // prints. Invoice references the Project by id only (CLAUDE.md §2), so this is two lookups
        // rather than a navigation.
        var project = await projectRepository.GetByIdAsync(invoice.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), invoice.ProjectId);

        var customer = await customerRepository.GetByIdAsync(project.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), project.CustomerId);

        // Pure string computation: the key never names the invoice number, the customer or anything a
        // caller supplied, so it cannot be steered and discloses nothing (D113).
        var documentFileUrl = $"invoices/{invoice.Id}/{Guid.NewGuid()}.pdf";

        // Self-guards: Draft, and a gross amount greater than zero (StateMachine.md §3.3). Runs before
        // anything is rendered or written, so a rejected send leaves no trace.
        invoice.Send(documentFileUrl);

        var document = InvoiceDocumentFactory.Create(invoice, customer, invoiceCalendar);
        var pdf = pdfGenerator.RenderInvoice(document);

        using (var content = new MemoryStream(pdf, writable: false))
        {
            await fileStorage.SaveAsync(content, documentFileUrl, cancellationToken);
        }

        var generated = tokenLinkService.Generate();
        var tokenLink = TokenLink.Create(TokenLinkEntityType.Invoice, invoice.Id, generated.Token, generated.ExpiresAt);
        await tokenLinkRepository.AddAsync(tokenLink, cancellationToken);

        // CancellationToken.None, deliberately: once the file is written, a client disconnect must not
        // cancel the commit in flight. A cancelled commit is an ambiguous one — the row may already be
        // committed.
        //
        // Only a ConflictException removes the file. Any other commit failure leaves it where it is:
        // whether the row committed is unknown — a connection lost after SQL Server committed raises
        // an exception all the same — and deleting a file the committed row points at would destroy
        // an issued invoice (BR-15). An orphaned file is the accepted cost of that uncertainty (D113).
        try
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (ConflictException)
        {
            // The only failure whose rollback is provable: UnitOfWork raises it for a lost concurrency
            // race (D96), where the second writer's UPDATE matched no row and the whole batch rolled
            // back. The file can belong to nothing, so it is removed.
            await TryRemoveUncommittedDocumentAsync(invoice.Id, documentFileUrl);
            throw;
        }

        // Logged against the Invoice, not the Project: unlike sending an Angebot — which drives
        // Lead.MarkAngebotSent() and is therefore a Lead-level milestone — this changes no other
        // aggregate's status at all. SRS FR-12.1 names "Invoice creation/status changes" in its own
        // right. After the commit, always (D50). Archiving is part of this milestone, not one of its
        // own, and the details name no storage key.
        await auditService.LogAsync(
            entityType: nameof(Invoice),
            entityId: invoice.Id,
            action: AuditAction.InvoiceSent,
            performedByUserId: command.SentByAdminId,
            details: $"Invoice {invoice.InvoiceNumber} sent to the customer.",
            cancellationToken);

        // After the commit, never before (CLAUDE.md §11) — this email hands the customer a working
        // credential and the issued document, so it must never describe a state that failed to persist.
        await emailSender.SendInvoiceReadyNotificationAsync(
            new InvoiceReadyNotification(
                invoice.Id,
                invoice.InvoiceNumber,
                customer.Name,
                customer.Email,
                invoice.GrossAmount.Amount,
                invoice.DueDate,
                generated.Token,
                pdf),
            cancellationToken);

        return invoice.ToDto();
    }

    /// <summary>
    /// Best-effort removal of a document whose Invoice row provably did not commit — called only after
    /// a <see cref="ConflictException"/>, never after an ambiguous failure (D113). It reduces orphans;
    /// it does not guarantee their absence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file was written by this request moments ago under a fresh GUID, so it cannot be anyone
    /// else's document — removing it can never delete an issued invoice. A failure here is logged and
    /// swallowed, so the original commit exception stays the caller's answer (D50's shape).
    /// </para>
    /// <para>
    /// <see cref="CancellationToken.None"/> on purpose: a disconnected client must not abandon the
    /// cleanup of a race it lost.
    /// </para>
    /// <para>
    /// <b>The log names the Invoice, never the key</b> (D113): the key is internal storage detail, and
    /// the orphan is findable from the invoice id because every key begins
    /// <c>invoices/{invoiceId}/</c>.
    /// </para>
    /// </remarks>
    private async Task TryRemoveUncommittedDocumentAsync(int invoiceId, string documentFileUrl)
    {
        try
        {
            await fileStorage.DeleteAsync(documentFileUrl, CancellationToken.None);
        }
        catch (Exception cleanupFailure)
        {
            logger.LogError(
                cleanupFailure,
                "Failed to remove the document archived for Invoice {InvoiceId} after its commit failed; it is now an orphaned file.",
                invoiceId);
        }
    }
}
