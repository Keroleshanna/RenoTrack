using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeAngebotReviewCommentRepository : IAngebotReviewCommentRepository
{
    public List<AngebotReviewComment> AddedComments { get; } = [];

    public Task AddAsync(AngebotReviewComment comment, CancellationToken cancellationToken)
    {
        AddedComments.Add(comment);
        return Task.CompletedTask;
    }
}
