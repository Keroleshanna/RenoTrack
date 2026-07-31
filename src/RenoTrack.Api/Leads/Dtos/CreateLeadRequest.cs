namespace RenoTrack.Api.Leads.Dtos;

/// <summary>
/// The public website contact form's payload (SRS FR-1.2/FR-1.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately narrower than <c>CreateLeadCommand</c>, which has seven parameters to this
/// record's five.</b> The two it omits are a security boundary, not a shape preference:
/// </para>
/// <para>
/// <c>Source</c> — if the caller supplied it, anyone could post <c>Source = Phone</c> to this
/// anonymous endpoint. That is not cosmetic: <c>CreateLeadCommandHandler</c> notifies the Admin
/// only when <c>Source == Website</c> (SRS FR-9.2), so a caller who controls this field can
/// suppress the notification for the Leads they create. The controller sets it.
/// </para>
/// <para>
/// <c>CreatedByUserId</c> — accepting it would let an anonymous caller attribute a Lead, and its
/// audit entry, to any user id they chose. It is <c>null</c> on this path (which <c>AuditLog</c>
/// already models as a system-triggered action) and will be the authenticated Admin's id on the
/// manual-entry path when that endpoint is built.
/// </para>
/// <para>
/// No validation attributes here on purpose: <c>CreateLeadCommandValidator</c> already covers these
/// fields, and a second mechanism firing first would produce a different error shape for the same
/// failure (CLAUDE.md §5, §22).
/// </para>
/// </remarks>
public sealed record CreateLeadRequest(
    string Name,
    string Phone,
    string Email,
    string? Address,
    string? Notes);
