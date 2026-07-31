using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Inspections.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.ScheduleInspection;
using RenoTrack.Application.Inspections.Dtos;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Inspection endpoints (Architecture.md §5.2, PermissionMatrix.md §2).
/// </summary>
/// <remarks>
/// Every Inspection operation lives here, including the one whose URL nests under Leads. That
/// action carries an absolute route template rather than moving to <c>LeadsController</c>:
/// scheduling, photo upload, and completion are three views of the same resource, and splitting one
/// of them onto another controller would mean a reader asking "how do Inspections work" has to find
/// two files, while <c>LeadsController</c> would take on a dependency it has no other use for.
/// Cohesion by resource beats cohesion by URL prefix.
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class InspectionsController(
    ICommandHandler<ScheduleInspectionCommand, InspectionDto> scheduleInspectionHandler) : ControllerBase
{
    /// <summary>
    /// Schedules an Inspection for a Lead (SRS FR-2.3). Admin only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PermissionMatrix.md</c> §2 marks this "F" for Admin and grants Inspector nothing, so
    /// authorization is purely role-based and there is <b>no</b> <c>IOwnershipValidator</c> call —
    /// using one for an "F" action would be a semantic error, not merely redundant (CLAUDE.md §16).
    /// </para>
    /// <para>
    /// Two side effects happen beyond creating the Inspection, both inside the handler: BR-13
    /// assigns the Inspector to the Lead, and the Lead transitions to <c>InspectionScheduled</c>.
    /// The Lead's own guard (status must be <c>New</c>) is what rejects a second scheduling attempt,
    /// surfacing as 409 through D59's mapping — the controller checks no status itself.
    /// </para>
    /// <para>
    /// Returns 201 without a <c>Location</c> header, and unlike Slice 5's Lead creation this is
    /// expected to stay that way for now: no <c>GET /api/v1/inspections/{id}</c> exists, it is
    /// absent from Architecture.md §5.2's endpoint table, and no agreed Phase 4 slice adds one —
    /// even though PermissionMatrix.md §2 does document a "View an Inspection" permission. That gap
    /// is recorded rather than closed by widening this slice.
    /// </para>
    /// </remarks>
    [HttpPost("/api/v1/leads/{leadId:int}/inspections")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<InspectionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Schedule(
        int leadId,
        ScheduleInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ScheduleInspectionCommand(
            leadId,
            request.ScheduledAt,
            request.InspectorId,
            // Who is acting — from the token, never the body (D61). Contrast InspectorId above,
            // which is who the work is assigned to and is therefore a legitimate input.
            ScheduledByAdminId: CurrentUserId());

        var inspection = await scheduleInspectionHandler.HandleAsync(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, inspection);
    }

    /// <summary>The authenticated caller's own user id, from the JWT's <c>sub</c> claim.</summary>
    /// <remarks>
    /// Throws rather than returning a sentinel: every action here requires a role, so an
    /// authenticated principal without a usable subject is an impossible state, and defaulting it to
    /// something like 0 would silently attribute the audit entry to a non-existent user.
    /// </remarks>
    private int CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(subject, out var userId)
            ? userId
            : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
    }
}
