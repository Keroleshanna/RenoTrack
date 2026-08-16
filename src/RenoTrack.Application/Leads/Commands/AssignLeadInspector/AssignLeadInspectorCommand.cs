namespace RenoTrack.Application.Leads.Commands.AssignLeadInspector;

/// <summary>
/// Assigns or reassigns the Inspector responsible for a Lead (<c>PermissionMatrix.md</c> §1
/// "Assign/reassign Inspector to a Lead — Admin F").
/// </summary>
/// <param name="InspectorId">
/// Who the work is being given to. A genuine input, not a claim about the caller: this is the
/// "who is acted upon" half of the distinction D61 was corrected with in Phase 4 Slice 7 — an
/// Admin assigning only themselves would make the feature pointless, exactly as it would for
/// <c>ScheduleInspectionRequest.InspectorId</c>.
/// </param>
/// <param name="AssignedByAdminId">
/// The acting Admin, from the JWT's subject claim, for the audit entry (D61).
/// </param>
/// <remarks>
/// <b>Standing this assignment up independently of scheduling a visit is the whole point of this
/// command.</b> BR-13 already assigns the Inspector as a side effect of
/// <c>ScheduleInspectionCommand</c>; §1's row exists for the other case — handing a Lead to a
/// colleague without booking anything, or moving it after the fact. No <c>LeadStatus</c> guard
/// applies, matching <c>Lead.AssignInspector</c>'s own documented reasoning.
/// </remarks>
public sealed record AssignLeadInspectorCommand(
    int LeadId,
    int InspectorId,
    int AssignedByAdminId);
