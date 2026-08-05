using RenoTrack.Application.Angebote.Queries.GetAngebotReviewComments;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Tests.Fakes;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Angebote.Queries.GetAngebotReviewComments;

/// <summary>
/// Access to the review history is governed by the <em>parent Angebot's</em> ownership, since a
/// comment carries only its author's id. These tests pin that the parent is loaded and checked
/// before the comments are read at all.
/// </summary>
public class GetAngebotReviewCommentsQueryHandlerTests
{
    private const int OwningInspectorId = 5;
    private const int OtherInspectorId = 6;

    private readonly FakeAngebotRepository _angebotRepository = new();
    private readonly FakeAngebotReviewCommentQueries _reviewCommentQueries = new();
    private readonly GetAngebotReviewCommentsQueryHandler _handler;

    public GetAngebotReviewCommentsQueryHandlerTests()
    {
        _handler = new GetAngebotReviewCommentsQueryHandler(
            _angebotRepository, _reviewCommentQueries, new OwnershipValidator());
    }

    private Angebot SeedAngebot() => _angebotRepository.Seed(
        Angebot.Create(leadId: 1, inspectionId: null, "ANG-2026-00001", OwningInspectorId));

    [Fact]
    public async Task HandleAsync_ReturnsTheCommentsForThatAngebot()
    {
        var angebot = SeedAngebot();
        _reviewCommentQueries.Result =
        [
            new(1, angebot.Id, AdminUserId: 3, "Please reprice section 2.", DateTime.UtcNow),
        ];

        var result = await _handler.HandleAsync(
            new GetAngebotReviewCommentsQuery(angebot.Id, OwningInspectorId), CancellationToken.None);

        var comment = Assert.Single(result);
        Assert.Equal("Please reprice section 2.", comment.Comment);
        Assert.Equal(angebot.Id, Assert.Single(_reviewCommentQueries.Calls));
    }

    /// <summary>Admin is "F" for the review history (PermissionMatrix.md §4).</summary>
    [Fact]
    public async Task HandleAsync_AdminReadsAnAngebotTheyDoNotOwn()
    {
        var angebot = SeedAngebot();

        var result = await _handler.HandleAsync(
            new GetAngebotReviewCommentsQuery(angebot.Id, RequestingInspectorId: null), CancellationToken.None);

        Assert.Empty(result);
        Assert.Single(_reviewCommentQueries.Calls);
    }

    [Fact]
    public async Task HandleAsync_NonOwningInspector_ThrowsForbiddenAndNeverReadsTheComments()
    {
        var angebot = SeedAngebot();

        await Assert.ThrowsAsync<ForbiddenException>(() => _handler.HandleAsync(
            new GetAngebotReviewCommentsQuery(angebot.Id, OtherInspectorId), CancellationToken.None));

        // The refusal happens before any comment is read — otherwise a 403 would still have
        // touched data the caller may not see.
        Assert.Empty(_reviewCommentQueries.Calls);
    }

    /// <summary>
    /// An unknown Angebot is a 404, not an empty list — which is the concrete reason the handler
    /// loads the parent rather than querying comments directly.
    /// </summary>
    [Fact]
    public async Task HandleAsync_UnknownAngebot_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.HandleAsync(
            new GetAngebotReviewCommentsQuery(999, OwningInspectorId), CancellationToken.None));

        Assert.Empty(_reviewCommentQueries.Calls);
    }
}
