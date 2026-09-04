namespace RenoTrack.Application.Inspections.Commands.ReassignInspection;

/// <summary>
/// Moves a scheduled Inspection to a different Inspector (<c>PermissionMatrix.md</c> §2 "Reassign
/// an Inspection to a different Inspector — Admin F, Inspector —").
/// </summary>
/// <param name="InspectorId">
/// Who the visit is being handed to — "who is acted upon", so a genuine body parameter under D61
/// as corrected in Phase 4 Slice 7, exactly like <c>ScheduleInspectionCommand.InspectorId</c>.
/// </param>
/// <param name="ReassignedByAdminId">The acting Admin, from the JWT, for the audit entry.</param>
public sealed record ReassignInspectionCommand(
    int InspectionId,
    int InspectorId,
    int ReassignedByAdminId);
