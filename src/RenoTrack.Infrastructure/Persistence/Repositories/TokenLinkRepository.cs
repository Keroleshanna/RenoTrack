using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

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
}
