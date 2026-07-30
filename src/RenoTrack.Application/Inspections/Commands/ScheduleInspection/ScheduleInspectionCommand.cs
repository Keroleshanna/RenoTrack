namespace RenoTrack.Application.Inspections.Commands.ScheduleInspection;

/// <summary>
/// Sequence Diagram §3 Step A. PermissionMatrix.md §2: scheduling is Admin-only, so unlike
/// CreateLeadCommand's nullable CreatedByUserId (which has a legitimate anonymous/website
/// path), <see cref="ScheduledByAdminId"/> is required — this action can never be anonymous
/// or system-triggered.
/// </summary>
public sealed record ScheduleInspectionCommand(int LeadId, DateTime ScheduledAt, int InspectorId, int ScheduledByAdminId);
