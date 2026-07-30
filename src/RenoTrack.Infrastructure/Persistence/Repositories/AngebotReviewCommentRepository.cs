using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Repositories;

/// <summary>AddAsync only — the interface has no GetByIdAsync, since no current command reads a comment back.</summary>
public sealed class AngebotReviewCommentRepository(RenoTrackDbContext dbContext) : IAngebotReviewCommentRepository
{
    public async Task AddAsync(AngebotReviewComment comment, CancellationToken cancellationToken) =>
        await dbContext.AngebotReviewComments.AddAsync(comment, cancellationToken);
}
