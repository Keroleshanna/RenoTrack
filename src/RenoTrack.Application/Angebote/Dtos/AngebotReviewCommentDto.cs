using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// One Admin review comment. SRS FR-5.4 requires review comments and status transitions to be
/// recorded in the Angebot's history, and PermissionMatrix.md §4 grants both roles read access to
/// that history ("View review comment history — Admin F, Inspector R").
/// </summary>
public sealed record AngebotReviewCommentDto(
    int Id,
    int AngebotId,
    int AdminUserId,
    string Comment,
    DateTime CreatedAt);

public static class AngebotReviewCommentMappingExtensions
{
    public static AngebotReviewCommentDto ToDto(this AngebotReviewComment comment) => new(
        comment.Id,
        comment.AngebotId,
        comment.AdminUserId,
        comment.Comment,
        comment.CreatedAt);
}
