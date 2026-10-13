using FluentValidation;

namespace RenoTrack.Application.Invoices.Queries.GetPublicInvoiceDocumentByToken;

/// <summary>
/// The archived PDF of an issued Invoice, for the customer holding its token link (Phase 14 Slice
/// 3b, <b>D114</b>, Wireframe A4's "[ Download PDF ]", PermissionMatrix.md §7). Possession of the
/// token is the entire authorisation model (Architecture.md §7.2), so this query carries no caller
/// identity. There is none.
/// </summary>
public sealed record GetPublicInvoiceDocumentByTokenQuery(string Token);

public sealed class GetPublicInvoiceDocumentByTokenQueryValidator
    : AbstractValidator<GetPublicInvoiceDocumentByTokenQuery>
{
    public GetPublicInvoiceDocumentByTokenQueryValidator()
    {
        RuleFor(q => q.Token).NotEmpty();
    }
}
