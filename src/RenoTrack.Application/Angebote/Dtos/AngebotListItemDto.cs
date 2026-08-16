using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Dtos;

/// <summary>
/// One row of the cross-Lead Angebot list (Phase 10's Angebote workspace and the Cockpit's
/// decision queue).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not <see cref="AngebotDto"/>.</b> That type is the create/transition response and
/// is keyed to one Angebot the caller already has context for, so it carries <c>LeadId</c> and no
/// customer name. A list is read without that context: a screen showing twenty rows cannot issue
/// twenty follow-up reads to discover who each one is for, and rendering a bare <c>LeadId</c> is
/// the defect CLAUDE.md §7 exists to prevent — a number that looks like data.
/// </para>
/// <para>
/// <c>LeadName</c> therefore comes from a join in the projection. That is exactly what a read-side
/// query interface is for (D36): the write model relates Angebot to Lead by id only, and nothing
/// here changes that — the join lives in the projection, never in the aggregate.
/// </para>
/// <para>
/// No sections, no items, no VAT breakdown: a list row shows totals, and <c>AngebotDetailDto</c>
/// already serves the one screen that needs the tree.
/// </para>
/// </remarks>
public sealed record AngebotListItemDto(
    int Id,
    string AngebotNumber,
    int LeadId,
    string LeadName,
    AngebotStatus Status,
    decimal NetTotal,
    decimal GrossTotal,
    int CreatedByInspectorId,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? DecisionAt);
