namespace RenoTrack.Api.Inspections.Dtos;

/// <summary>
/// Names the Inspector a scheduled visit is being handed to (<c>PermissionMatrix.md</c> §2).
/// </summary>
/// <remarks>
/// The single field is a legitimate body parameter under D61 because it identifies <em>who is
/// acted upon</em> rather than who is acting — the same reading that corrected
/// <c>ScheduleInspectionRequest.InspectorId</c> in Phase 4 Slice 7. The acting Admin comes from the
/// JWT, and an Admin is never an Inspector, so deriving this from the token would make the action
/// impossible to perform at all.
/// </remarks>
public sealed record ReassignInspectionRequest(int InspectorId);
