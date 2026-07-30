using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

public interface IAngebotReviewCommentRepository
{
    Task AddAsync(AngebotReviewComment comment, CancellationToken cancellationToken);
}
