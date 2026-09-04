namespace RenoTrack.Application.Inspections.Dtos;

/// <summary>
/// One Inspection with enough of its Lead to be actionable on site — the read shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first Inspection read in the system.</b> Before Phase 10 the API could schedule, annotate,
/// photograph and complete an Inspection but could not return one — not even by id. The C3 site
/// screen was therefore unable to load the visit it is named after, and no schedule could be shown
/// to anyone.
/// </para>
/// <para>
/// <b>Deliberately separate from <see cref="InspectionDto"/></b>, which is the command response and
/// maps straight off the aggregate via <c>ToDto()</c>. This one joins the Lead, so it cannot be a
/// mapping extension on the entity — the same split as <c>AngebotDto</c> vs. <c>AngebotDetailDto</c>.
/// </para>
/// <para>
/// Carries the Lead's name, address and phone because this is read **on a phone, on the way to a
/// building**. An id would be useless there; the address is the point of the record.
/// </para>
/// <para>
/// <b><c>PhotoCount</c> rather than the photos themselves.</b> <c>InspectionPhoto</c> holds a storage
/// key and there is still no authenticated endpoint serving those files (a known gap, CLAUDE.md
/// §13). Returning keys a caller cannot fetch would be worse than returning the count, which is what
/// a schedule actually needs.
/// </para>
/// </remarks>
public sealed record InspectionDetailDto(
    int Id,
    int LeadId,
    string LeadName,
    string? LeadAddress,
    string LeadPhone,
    DateTime ScheduledAt,
    int InspectorId,
    string? Notes,
    DateTime? CompletedAt,
    int PhotoCount);
