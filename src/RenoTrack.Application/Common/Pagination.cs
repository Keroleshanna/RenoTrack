namespace RenoTrack.Application.Common;

/// <summary>
/// The single source of truth for paging limits, so every list endpoint enforces the same bounds
/// and no slice re-invents them as magic numbers.
/// </summary>
/// <remarks>
/// A non-generic class deliberately: constants on <see cref="PagedResult{T}"/> would be reachable
/// only through a closed generic (<c>PagedResult&lt;LeadDto&gt;.MaxPageSize</c>), which reads as
/// though the limit varied by item type. It does not.
/// </remarks>
public static class Pagination
{
    /// <summary>Pages are 1-based, matching Architecture.md §5.1's <c>?page=</c> convention.</summary>
    public const int FirstPage = 1;

    public const int DefaultPageSize = 25;

    /// <summary>
    /// An upper bound exists so a caller cannot turn a paged endpoint back into an unbounded one by
    /// asking for everything at once — the reason Architecture.md §5.1 mandates pagination in the
    /// first place.
    /// </summary>
    public const int MaxPageSize = 100;
}
