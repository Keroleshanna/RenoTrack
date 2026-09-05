using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Three methods, each with a named consumer: <see cref="AddAsync"/> for <c>SendAngebotCommand</c>
/// and <c>ResendAngebotCommand</c>, <see cref="FindByTokenAsync"/> for the public read and decision
/// endpoints, and <see cref="FindCurrentForAngebotAsync"/> for the re-issue (Phase 11 Slice 6).
/// Nothing speculative — there is deliberately no <c>GetByIdAsync</c>, because a token link is
/// otherwise only ever reached by its token: an id-based lookup has no caller and would model the
/// wrong access path (CLAUDE.md §4).
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

    /// <summary>
    /// The link a re-issue would supersede: the most recently created one for this Angebot
    /// (FR-6.1a, D99).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Named for the business question, not as a generic <c>GetByEntityAsync</c></b> (§4). The
    /// caller wants "the one that would be superseded", and the repository is what knows that this
    /// means the newest — not the caller filtering a list.
    /// </para>
    /// <para>
    /// <b>Newest rather than "the only one", because Slice 6 makes more than one normal.</b>
    /// Superseded links are expired in place rather than deleted, so an Angebot accumulates rows
    /// with at most one usable. Returning a single row by assumption would turn that expected shape
    /// into an exception.
    /// </para>
    /// <para>
    /// Returns null when the Angebot has never been sent — an ordinary answer the handler turns
    /// into a refusal, not an exceptional one.
    /// </para>
    /// </remarks>
    Task<TokenLink?> FindCurrentForAngebotAsync(int angebotId, CancellationToken cancellationToken);
}
