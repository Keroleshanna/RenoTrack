using FluentValidation;

namespace RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;

/// <summary>
/// The read-only Invoice behind a token link (SRS FR-8.3, Wireframe A4, PermissionMatrix.md §7).
/// Possession of the token is the entire authorisation model (Architecture.md §7.2), so this query
/// carries no caller identity — there is none.
/// </summary>
public sealed record GetPublicInvoiceByTokenQuery(string Token);

public sealed class GetPublicInvoiceByTokenQueryValidator : AbstractValidator<GetPublicInvoiceByTokenQuery>
{
    public GetPublicInvoiceByTokenQueryValidator()
    {
        RuleFor(q => q.Token).NotEmpty();
    }
}
