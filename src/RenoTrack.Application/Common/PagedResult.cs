namespace RenoTrack.Application.Common;

/// <summary>
/// One page of a list-endpoint result (Architecture.md §5.1 mandates pagination on list endpoints).
/// </summary>
/// <remarks>
/// Lives in <c>Common</c> rather than a feature folder because it is genuinely feature-agnostic —
/// it names no DTO and depends on nothing below it, so this does not repeat the dependency-direction
/// mistake D23 corrected.
/// </remarks>
/// <param name="Items">The page's items, in the order the query produced them.</param>
/// <param name="Page">1-based page number this result represents.</param>
/// <param name="PageSize">Page size actually applied, which may differ from what a caller asked for.</param>
/// <param name="TotalCount">
/// Total matching rows across every page, not just this one — the client needs it to render page
/// controls, and it is the reason a list query costs two round trips rather than one.
/// </param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
