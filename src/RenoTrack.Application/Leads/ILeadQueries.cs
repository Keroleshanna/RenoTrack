using RenoTrack.Application.Common;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Leads;

/// <summary>
/// Read-side query interface for Lead — returns DTOs directly, never a hydrated aggregate
/// (Architecture.md §5.1's read/write split, D36). Lives in the Leads feature folder rather than
/// <c>Common.Interfaces</c> because its return type is a feature DTO, and <c>Common</c> must never
/// depend on a feature folder (D23) — the same placement as <c>ICatalogItemQueries</c>.
/// </summary>
/// <remarks>
/// Deliberately has no <c>GetByIdAsync</c>. Reading a single Lead must enforce
/// <c>PermissionMatrix.md</c> §1's scoped ("S") rule through <c>IOwnershipValidator</c>, which
/// operates on the Domain entity — so that path goes through <c>ILeadRepository</c> instead, and
/// costs nothing extra because <c>Lead</c> owns no children (its repository read is a single
/// <c>FindAsync</c>, the same query a projection would issue). The projection-over-hydration
/// rationale in D36 exists for aggregates with <c>Include</c> chains, which Lead is the opposite of.
/// </remarks>
public interface ILeadQueries
{
    /// <summary>
    /// The Lead pipeline (SRS FR-2.4, Wireframe B2): filterable by status, assigned Inspector, and
    /// creation date range — exactly the three filters that wireframe's filter row shows, no more.
    /// </summary>
    /// <param name="assignedInspectorId">
    /// <b>Also the scoping mechanism, not merely a filter.</b> <c>PermissionMatrix.md</c> §1 states
    /// an Inspector's pipeline is "filtered server-side"; the API layer sets this to the caller's
    /// own id for an Inspector, ignoring whatever they asked for, and passes an Admin's choice
    /// through untouched. This interface cannot tell the two cases apart, and deliberately does not
    /// need to — role decisions belong to the API layer (CLAUDE.md §16, D61).
    /// </param>
    Task<PagedResult<LeadDto>> GetPagedAsync(
        LeadStatus? status,
        int? assignedInspectorId,
        DateTime? createdFrom,
        DateTime? createdTo,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
