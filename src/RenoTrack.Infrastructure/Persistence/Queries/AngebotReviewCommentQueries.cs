using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Infrastructure.Persistence.Queries;

public sealed class AngebotReviewCommentQueries(RenoTrackDbContext dbContext) : IAngebotReviewCommentQueries
{
    public async Task<IReadOnlyList<AngebotReviewCommentDto>> GetForAngebotAsync(
        int angebotId,
        CancellationToken cancellationToken) =>
        await dbContext.AngebotReviewComments
            .AsNoTracking()
            .Where(c => c.AngebotId == angebotId)

            // Oldest first — a review thread reads forwards. Id breaks ties, since two comments
            // written in the same tick would otherwise be free to swap places between reads.
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new AngebotReviewCommentDto(
                c.Id,
                c.AngebotId,
                c.AdminUserId,
                c.Comment,
                c.CreatedAt))
            .ToListAsync(cancellationToken);
}
