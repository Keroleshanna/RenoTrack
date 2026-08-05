using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Two methods, both with a named consumer inside Phase 6's agreed slice plan: <see cref="AddAsync"/>
/// for <c>SendAngebotCommand</c> (Slice 2), <see cref="FindByTokenAsync"/> for the public read and
/// decision endpoints (Slices 3-4). Nothing speculative — there is deliberately no
/// <c>GetByIdAsync</c>, because a token link is only ever reached by its token: an id-based lookup
/// has no caller and would model the wrong access path (CLAUDE.md §4).
/// </summary>
public interface ITokenLinkRepository
{
    Task AddAsync(TokenLink tokenLink, CancellationToken cancellationToken);

    /// <summary>
    /// The single unauthenticated read path (ERD.md's unique index on <c>Token</c> calls it "the
    /// hottest unauthenticated read path"). Returns null when no such token exists — an unknown
    /// token is an ordinary outcome on a public endpoint, not an exceptional one.
    /// </summary>
    Task<TokenLink?> FindByTokenAsync(string token, CancellationToken cancellationToken);
}
