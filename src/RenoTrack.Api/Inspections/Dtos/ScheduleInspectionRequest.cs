namespace RenoTrack.Api.Inspections.Dtos;

/// <summary>
/// The Admin's request to schedule an Inspection against a Lead (SRS FR-2.3, Sequence Diagram §3).
/// </summary>
/// <param name="ScheduledAt">When the Inspector should visit.</param>
/// <param name="InspectorId">
/// <b>Who the visit is assigned to — a genuine input, not the caller's identity.</b> D61 requires
/// values describing <em>who the caller is</em> to be server-derived, and this is the opposite: an
/// Admin is choosing a third party to send. The caller's own id (<c>ScheduledByAdminId</c>) does
/// come from the JWT and is deliberately absent from this contract. The two must not be confused —
/// taking this from the token would make it impossible for an Admin to schedule anyone but
/// themselves, which is the entire operation.
/// </param>
public sealed record ScheduleInspectionRequest(DateTime ScheduledAt, int InspectorId);
