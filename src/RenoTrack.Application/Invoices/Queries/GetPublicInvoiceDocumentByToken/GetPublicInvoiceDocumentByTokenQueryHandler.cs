using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Queries.GetPublicInvoiceDocumentByToken;

/// <summary>
/// Serves the customer the document archived when their Invoice was sent (Phase 14 Slice 3b,
/// <b>D114</b>). The token-link twin of the Admin's <c>GetInvoiceDocumentQueryHandler</c> (D113).
/// </summary>
/// <remarks>
/// <para>
/// <b>It serves the archive and nothing else (BR-15).</b> This handler depends on
/// <see cref="IFileStorage"/> and not on <see cref="IPdfGenerator"/>. That dependency list is the
/// guarantee, and a test pins it: no path here can render. A key whose file has gone is a storage
/// defect, left to propagate as an unmapped 500 with no detail, and never repaired by regenerating.
/// </para>
/// <para>
/// <b>The token checks are the read's, inlined rather than shared</b> (Sequence Diagram §12 says
/// not to build a shared validator). An unknown token and a token for something other than an
/// Invoice are one branch and one message, so they cannot drift apart. Expiry is an honest 410.
/// <b>Prior use is not checked and the link is never consumed</b>: a download is a read, and
/// PermissionMatrix.md §7 grants no Invoice decision action. That is why this handler holds no
/// unit of work at all.
/// </para>
/// <para>
/// <b>Status does not matter.</b> Open, Paid and Void all serve the same archived bytes. The
/// document prints no status (D113 decision 2), and the Product Owner decided that a voided
/// invoice's document stays downloadable; the customer's page explains that it is cancelled.
/// </para>
/// <para>
/// <b>No archive is a 409</b> for an Invoice sent before Slice 3a, which has none and is never
/// given one (D113). The link resolves, and the Invoice's state is why there is nothing to serve.
/// This is the Admin read's answer too. Its message names nothing (no number, no id, no key)
/// because this caller is anonymous.
/// </para>
/// <para>
/// <b>No Project or Customer is loaded.</b> The file name needs only the invoice number, and a read
/// that touches less personal data is the better read.
/// </para>
/// </remarks>
public sealed class GetPublicInvoiceDocumentByTokenQueryHandler(
    IValidator<GetPublicInvoiceDocumentByTokenQuery> validator,
    ITokenLinkRepository tokenLinkRepository,
    IInvoiceRepository invoiceRepository,
    IFileStorage fileStorage) : IQueryHandler<GetPublicInvoiceDocumentByTokenQuery, InvoiceDocumentFile>
{
    /// <summary>
    /// The 409's text: fixed, and naming nothing an anonymous caller could not already see.
    /// </summary>
    internal const string NoDocumentMessage = "No PDF document is available for this invoice.";

    public async Task<InvoiceDocumentFile> HandleAsync(
        GetPublicInvoiceDocumentByTokenQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var tokenLink = await tokenLinkRepository.FindByTokenAsync(query.Token, cancellationToken);

        if (tokenLink is null || tokenLink.EntityType != TokenLinkEntityType.Invoice)
        {
            // The read's own text, so the two endpoints answer an invalid link identically.
            throw new NotFoundException(GetPublicInvoiceByTokenQueryHandler.NotValidMessage);
        }

        // DateTime.UtcNow, exactly as the read checks it. Changing this clock belongs to the separate
        // time-policy slice (NEXT_STEPS.md §8e), and a page and its download must never disagree
        // about whether one link has expired.
        if (tokenLink.IsExpired(DateTime.UtcNow))
        {
            throw new GoneException("This link has expired and can no longer be used.");
        }

        var invoice = await invoiceRepository.GetByIdAsync(tokenLink.EntityId, cancellationToken)
            ?? throw new NotFoundException(GetPublicInvoiceByTokenQueryHandler.NotValidMessage);

        if (invoice.DocumentFileUrl is null)
        {
            throw new ConflictException(NoDocumentMessage);
        }

        var content = await fileStorage.OpenReadAsync(invoice.DocumentFileUrl, cancellationToken);

        return new InvoiceDocumentFile(InvoiceDocumentFileName.For(invoice.InvoiceNumber), content);
    }
}
