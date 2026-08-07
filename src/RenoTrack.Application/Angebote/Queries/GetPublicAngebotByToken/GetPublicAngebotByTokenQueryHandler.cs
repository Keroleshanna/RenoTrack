using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;

/// <summary>
/// SRS FR-6.2 / Sequence Diagram §6 and §12. The read half of the token-link mechanism, and the
/// first use case in this system with no authenticated caller at all.
///
/// <para>
/// <b>Sequence Diagram §12's checks, minus one, deliberately.</b> §12 lists four: the token exists,
/// its entity type matches, it has not expired, and — "<i>for decision-type actions only</i>" —
/// that <c>UsedAt</c> is null. This is not a decision-type action, so <b>expiry is checked and
/// prior use is not</b>. That is BR-4 read literally: "Once a Lead has approved or rejected …
/// the same link cannot be reused for another state-changing action. <b>Viewing (read-only)
/// remains allowed.</b>" A customer who has already approved must still be able to re-read what
/// they agreed to.
/// </para>
/// <para>
/// <b>A wrong-entity-type token is a 404, not a distinct status.</b> Telling an anonymous caller
/// "that token is real, but it belongs to an Invoice" confirms the token's existence and leaks the
/// shape of the system for no benefit to anyone legitimately holding an Angebot link. Expiry is
/// different and is reported honestly as 410 — see <see cref="GoneException"/> for why that
/// distinction leaks nothing when the secret is 256 bits of CSPRNG output.
/// </para>
/// <para>
/// <b>No ownership check exists or could exist</b> — there is no principal to compare against.
/// Possession of the token is the entire authorisation model (Architecture.md §7.2), which is
/// exactly why the token is unguessable and why this endpoint is rate-limited (Slice 4).
/// </para>
/// </summary>
public sealed class GetPublicAngebotByTokenQueryHandler(
    IValidator<GetPublicAngebotByTokenQuery> validator,
    ITokenLinkRepository tokenLinkRepository,
    IAngebotRepository angebotRepository) : IQueryHandler<GetPublicAngebotByTokenQuery, PublicAngebotDto>
{
    public async Task<PublicAngebotDto> HandleAsync(
        GetPublicAngebotByTokenQuery query,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var tokenLink = await tokenLinkRepository.FindByTokenAsync(query.Token, cancellationToken);

        // One combined condition, so an Invoice token and an unknown token are literally the same
        // branch and cannot drift into producing distinguishable responses.
        if (tokenLink is null || tokenLink.EntityType != TokenLinkEntityType.Angebot)
        {
            // Message-only overload: the "id" here is the token, and a mapped exception's message
            // becomes both the ProblemDetails detail and a Warning log entry (D59).
            throw new NotFoundException("This link is not valid.");
        }

        if (tokenLink.IsExpired(DateTime.UtcNow))
        {
            throw new GoneException("This link has expired and can no longer be used.");
        }

        var angebot = await angebotRepository.GetByIdAsync(tokenLink.EntityId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), tokenLink.EntityId);

        return angebot.ToPublicDto();
    }
}
