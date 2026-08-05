using RenoTrack.Application.Angebote;
using RenoTrack.Application.Angebote.Dtos;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Records which Angebot ids it was asked about, so a test can prove the ownership refusal happens
/// <em>before</em> any comment is read rather than after.
/// </summary>
public sealed class FakeAngebotReviewCommentQueries : IAngebotReviewCommentQueries
{
    public List<int> Calls { get; } = [];
    public IReadOnlyList<AngebotReviewCommentDto> Result { get; set; } = [];

    public Task<IReadOnlyList<AngebotReviewCommentDto>> GetForAngebotAsync(
        int angebotId,
        CancellationToken cancellationToken)
    {
        Calls.Add(angebotId);
        return Task.FromResult(Result);
    }
}
