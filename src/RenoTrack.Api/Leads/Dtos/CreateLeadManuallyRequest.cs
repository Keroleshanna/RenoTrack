namespace RenoTrack.Api.Leads.Dtos;

/// <summary>
/// The channel an Admin logged a Lead from, for the manual-entry endpoint (SRS FR-2.1,
/// <c>PermissionMatrix.md</c> §1 "Create Lead manually (phone/email)").
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate API-layer enum rather than reusing <c>LeadSource</c>, so "an Admin cannot claim a
/// Lead arrived through the website" is structurally impossible instead of validated.</b>
/// <c>LeadSource.Website</c> is not a value this type can express, so no validator rule, no
/// controller <c>if</c>, and no test needs to defend the restriction — the shape does. That is
/// CLAUDE.md §2's "structurally impossible to construct in an invalid state" applied at the API
/// boundary, and it keeps the controller thin (§22) rather than making it decide anything.
/// </para>
/// <para>
/// The distinction matters for the same reason <c>CreateLeadRequest</c> omits <c>Source</c>
/// entirely (D61): <c>CreateLeadCommandHandler</c> sends the FR-9.2 Admin notification only when
/// <c>Source == Website</c>. An Admin typing a Lead in from a phone call is already at the
/// keyboard, so notifying them of their own action would be noise — but the reverse, letting this
/// endpoint mint a <c>Website</c> Lead, would fire a notification claiming an enquiry arrived
/// through a form nobody filled in.
/// </para>
/// <para>
/// Unknown values fail at model binding as a 400, because enums serialize and bind by name
/// (<c>JsonStringEnumConverter</c>, D61) rather than by ordinal.
/// </para>
/// </remarks>
public enum ManualLeadSource
{
    /// <summary>Logged after a phone call. Maps to <c>LeadSource.Phone</c>.</summary>
    Phone,

    /// <summary>Logged after an email exchange. Maps to <c>LeadSource.Email</c>.</summary>
    Email
}

/// <summary>
/// An Admin's manual Lead entry (SRS FR-2.1, Sequence Diagram §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wider than <c>CreateLeadRequest</c> by exactly one field, <c>Source</c>, and for a reason
/// that is the mirror image of why the public form omits it.</b> On the anonymous contact form the
/// channel is known from the endpoint itself, so accepting it would only let a caller lie. Here the
/// channel is genuinely the Admin's own knowledge — they are the only one who can say whether the
/// enquiry came by telephone or by email — so it is a real input, not a claim about the caller.
/// This is the same "who is acting" versus "what is being described" test D61 was corrected with in
/// Phase 4 Slice 7 for <c>ScheduleInspectionRequest.InspectorId</c>.
/// </para>
/// <para>
/// <c>CreatedByUserId</c> is still absent, and still for D61's original reason: it identifies the
/// caller, so it comes from the JWT's subject claim and never from the body. It is the one field
/// <c>CreateLeadCommand</c> has that neither request record carries.
/// </para>
/// <para>
/// No validation attributes, per CLAUDE.md §5/§22 — <c>CreateLeadCommandValidator</c> already
/// covers name, phone and email for both creation paths, and a second mechanism firing first would
/// give the same failure two different error shapes.
/// </para>
/// </remarks>
public sealed record CreateLeadManuallyRequest(
    string Name,
    string Phone,
    string Email,
    ManualLeadSource Source,
    string? Address,
    string? Notes);
