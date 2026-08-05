using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Application.Angebote;

/// <summary>
/// Read-side access to review comments.
/// </summary>
/// <remarks>
/// A separate interface from <see cref="IAngebotQueries"/> rather than another method on it:
/// <c>AngebotReviewComment</c> is its own aggregate, linked to <c>Angebot</c> by id alone with
/// neither type referencing the other (D-level rule in CLAUDE.md §2, verified by a structural test).
/// Folding its reads into the Angebot query interface would be the first place in the codebase where
/// that separation is blurred, for no gain.
/// </remarks>
public interface IAngebotReviewCommentQueries
{
    /// <summary>
    /// One Angebot's review history, oldest first — the order a reviewer reads a conversation in.
    /// </summary>
    /// <remarks>
    /// Takes no scope parameter, unlike <see cref="IAngebotQueries.GetForLeadAsync"/>: access is
    /// governed by the parent Angebot's ownership, which the handler establishes before calling this.
    /// </remarks>
    Task<IReadOnlyList<AngebotReviewCommentDto>> GetForAngebotAsync(
        int angebotId,
        CancellationToken cancellationToken);
}
