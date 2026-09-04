namespace RenoTrack.Application.Leads.Commands.UpdateLeadContactDetails;

/// <summary>
/// Corrects a Lead's contact details (<c>PermissionMatrix.md</c> §1 "Edit Lead contact details —
/// Admin F, Inspector S").
/// </summary>
/// <param name="RequestingInspectorId">
/// The Inspector the caller is, or <c>null</c> when the caller is an Admin. Not "the caller's user
/// id": §1 marks this row <c>F</c> for Admin, so an Admin is subject to no ownership rule and
/// passing their id here would invite a check that must not happen (CLAUDE.md §16). Supplied by
/// the API layer from the JWT, never from the request body (D61) — this is the same shape
/// <c>GetLeadByIdQuery</c> already uses for the identical Admin-F/Inspector-S split.
/// </param>
/// <param name="PerformedByUserId">
/// The caller's own user id, always present, used solely to attribute the audit entry.
/// </param>
/// <remarks>
/// <para>
/// <b>Why both id parameters exist, when for an Inspector they hold the same number.</b> They
/// answer different questions, and collapsing them would break one of the two. <c>Requesting-
/// InspectorId</c> answers "what may this caller reach", and its <c>null</c> is load-bearing — it
/// is how an Admin's <c>F</c> access is expressed, so it cannot also carry the Admin's identity.
/// <c>PerformedByUserId</c> answers "who did this", which the audit trail needs for an Admin every
/// bit as much as for an Inspector; deriving it from the scoping value would leave every
/// Admin-made correction attributed to nobody. Both are supplied by the API layer from the JWT and
/// neither is accepted from the request body (D61).
/// </para>
/// <para>
/// <c>Notes</c> is absent by design — see <c>Lead.UpdateContactDetails</c>, which explains why the
/// documented permission covers the four contact fields and not the enquiry description.
/// </para>
/// </remarks>
public sealed record UpdateLeadContactDetailsCommand(
    int LeadId,
    string Name,
    string Phone,
    string Email,
    string? Address,
    int? RequestingInspectorId,
    int PerformedByUserId);
