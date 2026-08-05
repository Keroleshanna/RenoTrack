using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Queries.GetAngebotReviewComments;

/// <summary>
/// Loads the parent Angebot first, then reads its comments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two round trips.</b> The comments are a sub-collection of one specific Angebot, so access
/// is governed by that Angebot's ownership rather than by anything on the comments themselves —
/// <c>AngebotReviewComment</c> carries no inspector id, only <c>AdminUserId</c> (the author). Loading
/// the parent is what makes <see cref="IOwnershipValidator"/> usable, and it also produces the
/// correct 404 for an Angebot that does not exist, which a bare comment query would report as an
/// empty list.
/// </para>
/// <para>
/// The alternative — scoping the comment query itself with a join back to <c>Angebote</c> — would
/// return an empty list to a non-owning Inspector rather than a 403, which is the wrong answer for a
/// single-resource sub-collection and inconsistent with <c>GET /angebote/{id}</c> next to it.
/// </para>
/// </remarks>
public sealed class GetAngebotReviewCommentsQueryHandler(
    IAngebotRepository angebotRepository,
    IAngebotReviewCommentQueries reviewCommentQueries,
    IOwnershipValidator ownershipValidator)
    : IQueryHandler<GetAngebotReviewCommentsQuery, IReadOnlyList<AngebotReviewCommentDto>>
{
    public async Task<IReadOnlyList<AngebotReviewCommentDto>> HandleAsync(
        GetAngebotReviewCommentsQuery query,
        CancellationToken cancellationToken)
    {
        var angebot = await angebotRepository.GetByIdAsync(query.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), query.AngebotId);

        // Null means Admin, who is "F" for the review history (PermissionMatrix.md §4).
        if (query.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        return await reviewCommentQueries.GetForAngebotAsync(query.AngebotId, cancellationToken);
    }
}
