using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// In-memory fake, no database. <see cref="FindByTokenAsync"/> reads only what was actually added,
/// so a handler that forgets to persist a link cannot appear to succeed here.
/// </summary>
public sealed class FakeTokenLinkRepository : ITokenLinkRepository
{
    public List<TokenLink> AddedTokenLinks { get; } = [];

    public Task AddAsync(TokenLink tokenLink, CancellationToken cancellationToken)
    {
        AddedTokenLinks.Add(tokenLink);
        return Task.CompletedTask;
    }

    public Task<TokenLink?> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(AddedTokenLinks.SingleOrDefault(t => t.Token == token));

    /// <summary>
    /// Newest first, matching the real repository. <c>Seed</c> assigns ascending ids, so ordering
    /// by id reproduces insertion order without depending on <c>CreatedAt</c> ticks, which are
    /// identical for links created inside one test.
    /// </summary>
    public Task<TokenLink?> FindCurrentForAngebotAsync(int angebotId, CancellationToken cancellationToken) =>
        Task.FromResult(AddedTokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefault());
}
