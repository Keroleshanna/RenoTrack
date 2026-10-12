using FluentValidation;

namespace RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;

/// <summary>
/// The archived PDF of an issued Invoice, for the Admin's download (Phase 14 Slice 3a, <b>D113</b>,
/// SRS FR-11.2). `PermissionMatrix.md` §5 marks it Admin <c>F</c> / Inspector <c>—</c>: the document
/// prints the customer's address, which D112 keeps Admin-only. The role gate is the controller's and
/// no ownership rule applies, so this query carries no caller identity at all (CLAUDE.md §16).
/// </summary>
public sealed record GetInvoiceDocumentQuery(int InvoiceId);

public sealed class GetInvoiceDocumentQueryValidator : AbstractValidator<GetInvoiceDocumentQuery>
{
    public GetInvoiceDocumentQueryValidator()
    {
        RuleFor(q => q.InvoiceId).GreaterThan(0);
    }
}

/// <summary>
/// An archived document on its way out: the file name it is delivered under and its bytes. Not a
/// JSON DTO — the controller writes <see cref="Content"/> as the response body and disposes it.
/// <b>It carries no storage key</b>; that stays internal to the Invoice and the storage boundary.
/// </summary>
public sealed record InvoiceDocumentFile(string FileName, Stream Content);
