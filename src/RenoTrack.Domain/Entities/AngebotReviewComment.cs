namespace RenoTrack.Domain.Entities;

/// <summary>
/// A single comment from Admin's internal review loop (SRS FR-5.4). Independent aggregate
/// root (Architecture.md §6's aggregate list intentionally does not list this as a child of
/// Angebot) — related to Angebot only by <see cref="AngebotId"/>, the same "related but not
/// owned" pattern as ContactMessage relates to Lead. This exists precisely because SRS FR-5.3
/// and Sequence Diagram §5 both confirm the review loop can repeat an arbitrary number of
/// times ("Loop repeats until Admin approves") — each cycle produces its own comment while
/// Angebot's own Status simply oscillates between Draft/InReview/ChangesRequested, so the
/// comment history must accumulate independently of the aggregate's current state.
///
/// Append-only by design (ERD.md: "Append-only log of the review loop") — no update or delete
/// method exists, matching Wireframe D3's "threaded" comment history and PermissionMatrix §4's
/// read access for both roles.
/// </summary>
public sealed class AngebotReviewComment
{
    public int Id { get; private set; }
    public int AngebotId { get; private set; }
    public int AdminUserId { get; private set; }
    public string Comment { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private AngebotReviewComment(int angebotId, int adminUserId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Comment is required.", nameof(comment));

        AngebotId = angebotId;
        AdminUserId = adminUserId;
        Comment = comment.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public static AngebotReviewComment Create(int angebotId, int adminUserId, string comment) =>
        new(angebotId, adminUserId, comment);
}
