using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Inspections.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Inspections.Commands.CompleteInspection;
using RenoTrack.Application.Inspections.Commands.ReassignInspection;
using RenoTrack.Application.Inspections.Commands.ReopenInspection;
using RenoTrack.Application.Inspections.Commands.ScheduleInspection;
using RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;
using RenoTrack.Application.Inspections.Commands.UploadInspectionPhoto;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Application.Inspections.Queries.GetInspections;

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
    ICommandHandler<ScheduleInspectionCommand, InspectionDto> scheduleInspectionHandler,
    ICommandHandler<UploadInspectionPhotoCommand, PhotoDto> uploadPhotoHandler,
    ICommandHandler<CompleteInspectionCommand, InspectionDto> completeInspectionHandler,
    ICommandHandler<UpdateInspectionNotesCommand, InspectionDto> updateNotesHandler,
    ICommandHandler<ReassignInspectionCommand, InspectionDto> reassignInspectionHandler,
    ICommandHandler<ReopenInspectionCommand, InspectionDto> reopenInspectionHandler,
    IQueryHandler<GetInspectionByIdQuery, InspectionDetailDto> getByIdHandler,
    IQueryHandler<GetInspectionScheduleQuery, IReadOnlyList<InspectionDetailDto>> getScheduleHandler)
    : ControllerBase
{
    /// <summary>
    /// One Inspection with its Lead's contact details. Admin "F"; an Inspector sees only their own.
    /// </summary>
    /// <remarks>
    /// <b>Phase 10 added this; there was no Inspection read of any kind before</b> — the C3 site
    /// screen could not load the visit it is named after, and no schedule could be shown to anyone.
    ///
    /// A non-owning Inspector receives <b>404, not 403</b>. The scope is a <c>WHERE</c> clause so the
    /// row is simply not found, and that is the right answer to give: a 403 would confirm that an
    /// Inspection with that id exists on someone else's round.
    /// </remarks>
    [HttpGet("{id:int}")]
    [ProducesResponseType<InspectionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var inspection = await getByIdHandler.HandleAsync(
            new GetInspectionByIdQuery(id, RequestingInspectorId()),
            cancellationToken);

        return Ok(inspection);
    }

    /// <summary>
    /// Site visits in a time window, earliest first — the operational schedule.
    /// </summary>
    /// <remarks>
    /// Unpaged: the window bounds the result, and one company's day of visits is a handful of rows.
    /// The window itself is capped by the validator, so this cannot become "every Inspection ever"
    /// by asking for a century.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InspectionDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSchedule(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] bool includeCompleted = true,
        CancellationToken cancellationToken = default)
    {
        var schedule = await getScheduleHandler.HandleAsync(
            new GetInspectionScheduleQuery(from, to, RequestingInspectorId(), includeCompleted),
            cancellationToken);

        return Ok(schedule);
    }

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
    /// Returns 201 without a <c>Location</c> header. <b>That gap is now closed:</b> Phase 10 added
    /// <c>GET /api/v1/inspections/{id}</c> (see <see cref="GetById"/>), which is the permission
    /// PermissionMatrix.md §2 always documented as "View an Inspection" and which nothing served.
    /// The header itself is left off deliberately rather than by omission — every other creation
    /// endpoint in this codebase behaves the same way, and changing one alone would be inconsistent.
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

    /// <summary>
    /// Uploads a photo to an Inspection (SRS FR-3.2, Sequence Diagram §3 Step B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inspector only — an Admin gets 403</b>, which inverts <see cref="Schedule"/>.
    /// <c>PermissionMatrix.md</c> §2 grants Admin nothing here, and states why: evidence should come
    /// from whoever was actually on site, keeping the chain of custody clear. The "S" marking means
    /// the assigned Inspector specifically, enforced by <c>IOwnershipValidator</c> in the handler.
    /// </para>
    /// <para>
    /// The inspector id is server-derived from the JWT (D61) — unlike <see cref="Schedule"/>'s
    /// <c>InspectorId</c>, because here the Inspector acts on their own Inspection rather than
    /// assigning work to a third party.
    /// </para>
    /// <para>
    /// The upload size limit is Kestrel's framework default (~30 MB); no project-specific limit is
    /// set because no document states one. Recorded so the effective cap is a known default rather
    /// than an assumed decision.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/photos")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<PhotoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadPhoto(
        int id,
        [FromForm] UploadInspectionPhotoRequest request,
        CancellationToken cancellationToken)
    {
        // The controller owns the stream's lifetime; the handler only reads from it. Disposing here
        // rather than in the handler keeps Application free of any assumption about where the
        // content came from.
        await using var content = request.File.OpenReadStream();

        var command = new UploadInspectionPhotoCommand(
            id,
            content,
            request.File.FileName,
            request.Caption,
            UploadedByInspectorId: CurrentUserId());

        var photo = await uploadPhotoHandler.HandleAsync(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, photo);
    }

    /// <summary>
    /// Records or revises an Inspection's notes (SRS FR-3.3, Sequence Diagram §3 Step B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inspector only, and specifically the assigned one</b> — <c>PermissionMatrix.md</c> §2 marks
    /// "Edit Inspection notes" as "— | S", the same shape as photo upload and completion. An Admin gets
    /// 403 from the role attribute; a non-owning Inspector gets 403 from <c>IOwnershipValidator</c>.
    /// </para>
    /// <para>
    /// <c>PATCH</c> rather than <c>POST</c>, matching Sequence Diagram §3's own route, and semantically
    /// correct: this is a partial update of an existing resource, not a new sub-resource or a state
    /// transition. It is genuinely <b>idempotent</b> — sending the same notes twice leaves the same
    /// state — which is the opposite of <see cref="Complete"/>, where a repeat is a 409. No guard
    /// against repeat submission is invented here, because for an edit a repeat is legitimate.
    /// </para>
    /// <para>
    /// <b>Sending <c>null</c> clears the notes</b>, deliberately. `Inspection.UpdateNotes` accepts null
    /// and the command's validator places no rule on the field, so clearing is a supported operation
    /// rather than an edge case to reject.
    /// </para>
    /// <para>
    /// BR-10 makes a completed Inspection immutable, enforced by the aggregate's own guard inside
    /// <c>UpdateNotes</c> and surfacing as 409 through D59 — the controller checks no status itself. No
    /// audit entry is written: editing notes is operational activity, not a workflow milestone (§10),
    /// the same classification photo upload carries.
    /// </para>
    /// <para>
    /// This endpoint closes a gap recorded since Slice 7: <c>UpdateInspectionNotesCommand</c> has existed,
    /// registered and tested, since Phase 2 while no HTTP route reached it. `Architecture.md` §5.2 omitted
    /// the route that `PermissionMatrix.md` §2 and Sequence Diagram §3 both documented; that omission is
    /// corrected in the same commit as this action, per the documentation-first rule (CLAUDE.md §15).
    /// </para>
    /// </remarks>
    [HttpPatch("{id:int}")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<InspectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateNotes(
        int id,
        UpdateInspectionNotesRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateInspectionNotesCommand(
            id,
            request.Notes,
            // Who is acting — from the token, never the body (D61).
            UpdatedByInspectorId: CurrentUserId());

        var inspection = await updateNotesHandler.HandleAsync(command, cancellationToken);

        return Ok(inspection);
    }

    /// <summary>
    /// Marks an Inspection complete (SRS FR-3.4, Sequence Diagram §3 Step B, Architecture.md §5.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inspector only, and specifically the assigned one.</b> <c>PermissionMatrix.md</c> §2 marks
    /// this "— | S", so an Admin gets 403 from the role attribute and a non-owning Inspector gets 403
    /// from <c>IOwnershipValidator</c> inside the handler — the same shape as <see cref="UploadPhoto"/>
    /// and the inverse of <see cref="Schedule"/>.
    /// </para>
    /// <para>
    /// <b>No request record exists, deliberately.</b> D61 says the wire contract is a strict subset of
    /// the command's parameters and that a request type is justified exactly when that subset differs.
    /// Here the subset is empty: <c>InspectionId</c> comes from the route and <c>CompletedByInspectorId</c>
    /// is the caller's own identity from the JWT's <c>sub</c> claim. This is the "who is acting" case,
    /// unlike <see cref="Schedule"/>'s <c>InspectorId</c>, which names a third party. The absence of a
    /// DTO here is a finding about this use case, not a forgotten file.
    /// </para>
    /// <para>
    /// <b>Two aggregates change and they change atomically.</b> The handler completes the Inspection and
    /// moves the Lead to <c>InspectionDone</c>, then calls <c>IUnitOfWork.SaveChangesAsync</c> once.
    /// Both repositories and the unit of work share the one request-scoped <c>RenoTrackDbContext</c>, so
    /// the two <c>UPDATE</c>s ride EF Core's single implicit transaction — either both land or neither
    /// does. This is genuine atomicity, unlike Slice 8's photo upload, which spans a filesystem and a
    /// database and can only compensate. <b>The audit entry is not part of it</b>: <c>IAuditService</c>
    /// is best-effort and commits independently after that transaction (D50), so a completed Inspection
    /// with no audit row is possible and accepted.
    /// </para>
    /// <para>
    /// If the Lead's own guard rejects the transition, the Inspection has already been mutated in memory
    /// by the preceding <c>Complete()</c> call. Nothing is written, because <c>SaveChangesAsync</c> is
    /// never reached and the request-scoped <c>DbContext</c> is disposed with its change tracker when the
    /// request ends. That safety comes from the scope lifetime, not from a guard — it holds only while no
    /// handler shares a <c>DbContext</c> across two units of work and none saves after catching a Domain
    /// exception. Neither happens today (CLAUDE.md §17: Domain exceptions propagate unwrapped).
    /// </para>
    /// <para>
    /// Repeated completion is <b>not</b> idempotent: the second attempt is rejected by the Inspection's
    /// own guard as 409, and the original <c>CompletedAt</c> is never overwritten. Silently returning 200
    /// would hide a real client bug (a double-tap on the mobile browser SRS §90 anticipates), and
    /// rewriting the timestamp would undo the evidentiary value BR-10 exists to protect.
    /// </para>
    /// <para>
    /// <b>Nothing about the Inspection's content is required first.</b> No photo minimum and no notes
    /// requirement exists anywhere — SRS FR-3.2 grants a capability rather than stating a precondition,
    /// FR-3.4 states only the consequence, StateMachine.md §1.3's guard is "Inspection belongs to this
    /// Lead", and no BusinessRules.md entry mentions either. Inventing one here would need a numbered
    /// business rule first.
    /// </para>
    /// <para>
    /// Returns 200 with the updated <see cref="InspectionDto"/> (Sequence Diagram §3 shows 200 OK) — not
    /// 201, since nothing is created, and not 204, since the completion timestamp is server-generated and
    /// no <c>GET /api/v1/inspections/{id}</c> exists to fetch it from. The DTO carries no Lead field, so a
    /// client that needs the Lead's new status re-reads <c>GET /api/v1/leads/{id}</c> (Slice 6) rather
    /// than this response growing a field for one endpoint.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<InspectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken)
    {
        var command = new CompleteInspectionCommand(
            id,
            // Who is acting — from the token, never the body (D61).
            CompletedByInspectorId: CurrentUserId());

        var inspection = await completeInspectionHandler.HandleAsync(command, cancellationToken);

        return Ok(inspection);
    }

    /// <summary>
    /// Reopens a completed Inspection so its record can be corrected, then completed again
    /// (BR-10, Phase 10). Assigned Inspector only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the action BR-10 names, not a relaxation of it.</b> The rule states that a
    /// completed Inspection is immutable and that changing one "requires a distinct, explicit
    /// action (e.g. a 'reopen' use case), not implicit editing". Photo upload, notes and
    /// reassignment still refuse outright while the visit is complete; this is the explicit way to
    /// say otherwise, and it is audited.
    /// </para>
    /// <para>
    /// Inspector-only, matching every other Inspection edit: <c>PermissionMatrix.md</c> §2 marks
    /// photos, notes and completion Inspector <c>S</c> / Admin <c>—</c>, deliberately, so the
    /// evidence chain-of-custody points at whoever was on site. The action that *enables* those
    /// edits belongs to the same person, and ownership is enforced in the handler.
    /// </para>
    /// <para>
    /// <b>The Lead does not move back</b> — it stays at <c>InspectionDone</c>, because the visit
    /// did happen and any Angebot built from it stays valid. <b>409</b> if the visit was never
    /// completed.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/reopen")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<InspectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(int id, CancellationToken cancellationToken)
    {
        var inspection = await reopenInspectionHandler.HandleAsync(
            new ReopenInspectionCommand(InspectionId: id, ReopenedByInspectorId: CurrentUserId()),
            cancellationToken);

        return Ok(inspection);
    }

    /// <summary>
    /// Moves a scheduled Inspection to a different Inspector (<c>PermissionMatrix.md</c> §2
    /// "Reassign an Inspection to a different Inspector — Admin F, Inspector —").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Admin-only, narrowing the class-level attribute. Marked <c>F</c>, so no ownership check
    /// belongs here (CLAUDE.md §16) — the outgoing Inspector's ownership is the very thing being
    /// removed, so consulting it would be backwards.
    /// </para>
    /// <para>
    /// <c>InspectorId</c> comes from the body because it names <em>who is acted upon</em>, matching
    /// <c>ScheduleInspectionRequest.InspectorId</c> (D61 as corrected in Phase 4 Slice 7). The
    /// acting Admin still comes from the JWT.
    /// </para>
    /// <para>
    /// The handler also re-applies BR-13, so the Lead's assigned Inspector moves with the visit in
    /// the same commit. <b>409</b> once the Inspection is completed — BR-10 makes it immutable, and
    /// rewriting who attended a finished visit would falsify the record. <b>404</b> when the target
    /// names no active Inspector (D62), which a foreign key alone could not determine.
    /// </para>
    /// </remarks>
    [HttpPut("{id:int}/inspector")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<InspectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reassign(
        int id,
        ReassignInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var inspection = await reassignInspectionHandler.HandleAsync(
            new ReassignInspectionCommand(
                InspectionId: id,
                request.InspectorId,
                ReassignedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(inspection);
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

    /// <summary>
    /// The caller's own id when they are an Inspector, or <c>null</c> when they are an Admin and
    /// therefore unrestricted ("F" in <c>PermissionMatrix.md</c> §2).
    /// </summary>
    /// <remarks>
    /// <b>Written to fail secure</b>, identical in shape to the helpers on <c>LeadsController</c> and
    /// <c>AngeboteController</c>: <c>null</c> means unrestricted, so it is only ever returned after
    /// positively establishing the Admin role — never by falling through. Inspector is checked first,
    /// so a mis-provisioned dual-role account is scoped rather than unrestricted. Anything that is
    /// neither role is refused outright rather than defaulted.
    /// </remarks>
    private int? RequestingInspectorId()
    {
        if (User.IsInRole(Roles.Inspector))
        {
            return CurrentUserId();
        }

        if (User.IsInRole(Roles.Admin))
        {
            return null;
        }

        throw new ForbiddenException("This account has no role that grants access to Inspections.");
    }
}
