using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;

/// <summary>
/// SRS FR-8.3 / Sequence Diagram §9 and §12. The customer's read-only view of what they owe — the
/// second unauthenticated surface in this system, and structurally the twin of
/// <c>GetPublicAngebotByTokenQueryHandler</c>.
///
/// <para>
/// <b>Sequence Diagram §12's checks, minus one, deliberately.</b> §12 lists four: the token exists,
/// its entity type matches, it has not expired, and — "<i>for decision-type actions only</i>" —
/// that <c>UsedAt</c> is null. This is not a decision-type action, so **expiry is checked and prior
/// use is not**. For an Invoice link that check would in fact be unreachable: `PermissionMatrix.md`
/// §7 grants the customer "View Invoice via token link" and nothing else, so **no Invoice
/// decision action exists at all** and <c>UsedAt</c> is never set on one. BR-4 mentions "an
/// Invoice's decision-type action" hypothetically; none is documented, and none is invented here.
/// </para>
/// <para>
/// <b>A wrong-entity-type token is a 404, not a distinct status</b> — one combined condition, so an
/// Angebot token and an unknown token are literally the same branch and cannot drift into producing
/// distinguishable responses. Telling an anonymous caller "that token is real, but it belongs to an
/// Angebot" confirms the token's existence for no legitimate benefit. Expiry is different and is
/// reported honestly as 410.
/// </para>
/// <para>
/// <b>A <c>Draft</c> Invoice is unreachable here</b> without a guard, because a token link only
/// comes into existence when the Invoice is sent. No status check is therefore written — one would
/// be unreachable code, and CLAUDE.md §6 forbids a handler re-checking aggregate state anyway.
/// </para>
/// <para>
/// <b>No ownership check exists or could exist</b> — there is no principal to compare against.
/// Possession of the token is the authorisation model, which is exactly why the token is unguessable
/// and why this endpoint sits behind the public rate limiter (D65).
/// </para>
/// </summary>
public sealed class GetPublicInvoiceByTokenQueryHandler(
    IValidator<GetPublicInvoiceByTokenQuery> validator,
    ITokenLinkRepository tokenLinkRepository,
    IInvoiceRepository invoiceRepository,
    IProjectRepository projectRepository,
    ICustomerRepository customerRepository) : IQueryHandler<GetPublicInvoiceByTokenQuery, PublicInvoiceDto>
{
    public async Task<PublicInvoiceDto> HandleAsync(
        GetPublicInvoiceByTokenQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var tokenLink = await tokenLinkRepository.FindByTokenAsync(query.Token, cancellationToken);

        if (tokenLink is null || tokenLink.EntityType != TokenLinkEntityType.Invoice)
        {
            // Message-only overload: the "id" here would be the token itself, and a mapped
            // exception's message becomes both the ProblemDetails detail and a Warning log entry
            // (D59) — neither of which may ever contain a live credential.
            throw new NotFoundException("This link is not valid.");
        }

        if (tokenLink.IsExpired(DateTime.UtcNow))
        {
            throw new GoneException("This link has expired and can no longer be used.");
        }

        var invoice = await invoiceRepository.GetByIdAsync(tokenLink.EntityId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), tokenLink.EntityId);

        // Wireframe A4 renders the customer's name in its header, and an Invoice cannot see a
        // Customer (CLAUDE.md §2) — so the name is resolved here and passed to the mapper.
        var project = await projectRepository.GetByIdAsync(invoice.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), invoice.ProjectId);

        var customer = await customerRepository.GetByIdAsync(project.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), project.CustomerId);

        return invoice.ToPublicDto(customer.Name);
    }
}
