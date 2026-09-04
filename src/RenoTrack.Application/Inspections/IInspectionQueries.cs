using RenoTrack.Application.Inspections.Dtos;

namespace RenoTrack.Application.Inspections;

/// <summary>
/// Read-side access to Inspections (D36). New in Phase 10 — see <see cref="InspectionDetailDto"/> for why
/// there was none before.
/// </summary>
public interface IInspectionQueries
{
    /// <summary>
    /// One Inspection with its Lead's contact details, or <see langword="null"/> if no such id
    /// exists.
    /// </summary>
    /// <param name="requestingInspectorId">
    /// The Inspector whose scope applies, or <see langword="null"/> for an Admin ("F" in
    /// PermissionMatrix.md §2, against the Inspector's "S"). Applied as a <c>WHERE</c> clause, so a
    /// non-owning Inspector receives <see langword="null"/> and the caller turns that into a 404
    /// rather than a 403 — a single-resource read that leaked "this exists but is not yours" would
    /// tell an Inspector about another's assignment.
    /// </param>
    Task<InspectionDetailDto?> GetByIdAsync(
        int id,
        int? requestingInspectorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inspections scheduled within a window, earliest first — the operational schedule behind the
    /// Cockpit's day plan and the Besichtigungen workspace.
    /// </summary>
    /// <remarks>
    /// Unpaged, and deliberately: the caller supplies the window, so the result is bounded by time
    /// rather than by a page size. A day or a week of site visits for one company is a handful of
    /// rows, and paging it would make "today's schedule" a multi-request operation.
    /// </remarks>
    /// <param name="from">Inclusive lower bound on <c>ScheduledAt</c>.</param>
    /// <param name="to">Exclusive upper bound, so a caller can ask for exactly one day.</param>
    /// <param name="includeCompleted">
    /// Whether visits already marked complete are returned. A day plan wants them (they show what
    /// has been done); a "what still needs doing" read does not.
    /// </param>
    Task<IReadOnlyList<InspectionDetailDto>> GetScheduledAsync(
        DateTime from,
        DateTime to,
        int? requestingInspectorId,
        bool includeCompleted,
        CancellationToken cancellationToken);
}
