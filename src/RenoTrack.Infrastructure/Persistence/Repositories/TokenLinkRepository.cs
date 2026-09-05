using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>
/// TokenLink owns no children, so <see cref="FindByTokenAsync"/> needs no Include chain and the
/// "full aggregate" rule (CLAUDE.md §4) is satisfied by the row alone.
///
/// Deliberately tracking, not AsNoTracking, even though the read endpoint (Slice 3) never mutates:
/// the decision endpoint (Slice 4) loads through this same method and then calls MarkUsed(), and
/// this project has no UpdateAsync anywhere — persistence depends entirely on the change tracker
/// seeing the loaded instance. A no-tracking read here would make that mutation silently vanish at
/// SaveChangesAsync, which is precisely the failure mode LeadRepository's own doc comment warns of.
/// </summary>
public sealed class TokenLinkRepository(RenoTrackDbContext dbContext) : ITokenLinkRepository
{
    public async Task AddAsync(TokenLink tokenLink, CancellationToken cancellationToken) =>
        await dbContext.TokenLinks.AddAsync(tokenLink, cancellationToken);

    public async Task<TokenLink?> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
        await dbContext.TokenLinks.FirstOrDefaultAsync(t => t.Token == token, cancellationToken);

    /// <summary>
    /// Ordered <c>CreatedAt DESC, Id DESC</c> and taking the first — never <c>SingleAsync</c>.
    /// </summary>
    /// <remarks>
    /// More than one row per Angebot is the normal shape as of Slice 6 (D99), because a re-issue
    /// expires the previous link rather than deleting it. <c>Id</c> breaks the tie because
    /// <c>CreatedAt</c> is not unique — two rows created inside the same clock tick would otherwise
    /// order arbitrarily, and "which link is current" must never be arbitrary. This mirrors the
    /// ordering <c>NotificationRetryExecutor</c> already uses for the same reason.
    /// </remarks>
    public async Task<TokenLink?> FindCurrentForAngebotAsync(int angebotId, CancellationToken cancellationToken) =>
        await dbContext.TokenLinks
            .Where(t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == angebotId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
