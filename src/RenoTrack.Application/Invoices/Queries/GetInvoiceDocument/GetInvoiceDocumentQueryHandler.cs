using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;

/// <summary>
/// Serves the document archived when the Invoice was sent (Phase 14 Slice 3a, <b>D113</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It serves the archive and nothing else (BR-15).</b> No path here renders a PDF: an issued
/// invoice is the file archived at send, and re-deriving it would print today's customer address and
/// today's company identity under an old date. That is why this handler depends on
/// <see cref="IFileStorage"/> and not on <see cref="IPdfGenerator"/> — the dependency list is the
/// guarantee.
/// </para>
/// <para>
/// <b>Three outcomes besides the file.</b> An unknown invoice is 404. An invoice with no archived
/// document — a <c>Draft</c>, or one sent before Slice 3a, which is never backfilled — is 409: it
/// exists, and its state is why there is nothing to serve. A key whose file is missing is a storage
/// defect and is left to propagate as an unmapped 500; it is never repaired by regenerating.
/// </para>
/// <para>
/// <b>Status does not matter.</b> A <c>Paid</c> or <c>Void</c> invoice's document is still exactly
/// what was issued, and BR-9 keeps the record (D113).
/// </para>
/// <para>
/// Reads through <see cref="IInvoiceRepository"/> rather than a projection, as the single-Lead read
/// does (CLAUDE.md §22): one aggregate by id, and the key never leaves this method.
/// </para>
/// </remarks>
public sealed class GetInvoiceDocumentQueryHandler(
    IValidator<GetInvoiceDocumentQuery> validator,
    IInvoiceRepository invoiceRepository,
    IFileStorage fileStorage) : IQueryHandler<GetInvoiceDocumentQuery, InvoiceDocumentFile>
{
    public async Task<InvoiceDocumentFile> HandleAsync(
        GetInvoiceDocumentQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var invoice = await invoiceRepository.GetByIdAsync(query.InvoiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), query.InvoiceId);

        if (invoice.DocumentFileUrl is null)
        {
            throw new ConflictException(
                $"Invoice {invoice.InvoiceNumber} has no issued document: it has not been sent, or it was "
                + "sent before invoice documents were archived.");
        }

        var content = await fileStorage.OpenReadAsync(invoice.DocumentFileUrl, cancellationToken);

        return new InvoiceDocumentFile(InvoiceDocumentFileName.For(invoice.InvoiceNumber), content);
    }
}
