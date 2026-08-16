namespace RenoTrack.Api.Leads.Dtos;

/// <summary>
/// The complete set of a Lead's contact fields (<c>PermissionMatrix.md</c> §1 "Edit Lead contact
/// details"). Sent to <c>PUT /api/v1/leads/{id}</c>, which replaces all four.
/// </summary>
/// <remarks>
/// <para>
/// Carries neither the caller's identity nor their scope: both come from the JWT (D61). It is
/// therefore a strict subset of <c>UpdateLeadContactDetailsCommand</c>, which is the normal shape
/// for a request record in this codebase (CLAUDE.md §22).
/// </para>
/// <para>
/// <c>Notes</c> is absent, matching the Domain method — §1 grants editing of contact details, and
/// FR-2.1 lists notes as a separate field. See <c>Lead.UpdateContactDetails</c> for the full
/// reasoning and why the resulting gap is recorded rather than quietly closed.
/// </para>
/// <para>
/// No validation attributes: <c>UpdateLeadContactDetailsCommandValidator</c> owns the field rules,
/// and a second mechanism firing first would give one failure two different error shapes.
/// </para>
/// </remarks>
public sealed record UpdateLeadContactDetailsRequest(
    string Name,
    string Phone,
    string Email,
    string? Address);

/// <summary>
/// Names the Inspector a Lead is being handed to (<c>PermissionMatrix.md</c> §1).
/// </summary>
/// <remarks>
/// The single field is a legitimate body parameter under D61 because it identifies <em>who is
/// acted upon</em> rather than who is acting — the same reading that corrected
/// <c>ScheduleInspectionRequest.InspectorId</c> in Phase 4 Slice 7. Deriving it from the token
/// would restrict an Admin to assigning Leads to themselves, and Admins are not Inspectors.
/// </remarks>
public sealed record AssignLeadInspectorRequest(int InspectorId);
