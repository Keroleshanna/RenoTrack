using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Api.Projects.Dtos;
using RenoTrack.Application.Projects.Commands.CompleteProject;
using RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Application.Projects.Queries.GetProjectById;
using RenoTrack.Application.Projects.Queries.GetProjectInvoiceBalance;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Project endpoints (Architecture.md §5.2, PermissionMatrix.md §5).
/// </summary>
/// <remarks>
/// <para>
/// The conversion route nests under Angebote but lives here, following the precedent
/// <c>AngeboteController</c> set for <c>POST /api/v1/leads/{leadId}/angebote</c>: cohesion by
/// resource beats cohesion by URL prefix, and putting the one action that creates a Project onto
/// <c>AngeboteController</c> would give that controller a dependency it has no other use for.
/// Architecture.md §5.2 names the route; `PROJECT_ROADMAP.md` names this controller.
/// </para>
/// <para>
/// <b>Writing is Admin-only; reading is open to both roles and unscoped.</b> PermissionMatrix.md §5
/// marks "Convert Angebot to Project" Admin <c>F</c> and "View Project detail" Admin <c>F</c> /
/// Inspector <c>R</c>. <c>R</c> is read-only but **not** scoped — the matrix's own note explains
/// why ("Inspector can view, e.g. to see the outcome of a Lead they worked, but not act on it") —
/// so **no <c>IOwnershipValidator</c> call exists anywhere in this feature**, and none should be
/// added. Both actions are <c>F</c>/<c>R</c>, never <c>S</c>; an ownership check here would be a
/// semantic error rather than merely redundant (CLAUDE.md §16).
/// </para>
/// <para>
/// Wireframe E1 heads its Project-detail screen "Roles: Admin", which reads as a contradiction of
/// the Inspector's <c>R</c>. It is the same divergence D3 already carries, and Phase 5 settled it
/// the same way: <c>PermissionMatrix.md</c> is the authority on permissions (CLAUDE.md §16 says to
/// decide from its letter), while a wireframe's "Roles" line names the screen's primary audience.
/// <c>GetReviewComments</c> admits both roles for exactly this reason.
/// </para>
/// <para>
/// <b><c>Complete</c> arrived in Phase 8 Slice 6; <c>PutOnHold</c> and <c>Resume</c> still have no
/// endpoint.</b> The Domain carries all three (StateMachine.md §4.3), but on-hold/resume are
/// assigned to no phase at all and Phase 8 deliberately does not claim them (G-8).
/// <b>Consequence worth knowing:</b> <c>Project.Complete()</c> refuses anything but <c>Active</c>,
/// so if on-hold ever ships without resume, an <c>OnHold</c> Project would be unable to be either
/// resumed or completed. Unreachable today only because nothing can put a Project on hold either.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class ProjectsController(
    ICommandHandler<ConvertAngebotToProjectCommand, ProjectDto> convertHandler,
    ICommandHandler<CompleteProjectCommand, ProjectDto> completeProjectHandler,
    IQueryHandler<GetProjectByIdQuery, ProjectDetailDto> getProjectByIdHandler,
    IQueryHandler<GetProjectInvoiceBalanceQuery, ProjectInvoiceBalanceDto> getInvoiceBalanceHandler) : ControllerBase
{
    /// <summary>
    /// Converts a customer-approved Angebot into a Project (SRS FR-7.1, BR-2). Admin only.
    /// </summary>
    /// <remarks>
    /// Every guard lives below this method and is deliberately not duplicated here (CLAUDE.md §22).
    /// BR-2 — only a <c>CustomerApproved</c> Angebot may convert — and "this Angebot has already
    /// been converted" both surface as 409 through <c>ConflictException</c>. The Admin's id comes
    /// from the token's subject claim and the Angebot's from the route, so **this endpoint has no
    /// request body at all** (D61): there is no value a caller could legitimately supply.
    /// </remarks>
    [HttpPost("/api/v1/angebote/{id:int}/convert-to-project")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConvertFromAngebot(int id, CancellationToken cancellationToken)
    {
        var project = await convertHandler.HandleAsync(
            new ConvertAngebotToProjectCommand(id, PerformedByAdminId: CurrentUserId()),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
    }

    /// <summary>
    /// Marks a Project Completed (SRS FR-7.3, FR-8.6, StateMachine.md §4.3, Sequence Diagram §10).
    /// Admin only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every guard lives below this method (CLAUDE.md §22). <b>409</b> covers two distinct
    /// refusals: the Project's Invoices block it (it has none at all, or one or more is
    /// <c>Draft</c>/<c>Sent</c>/<c>Overdue</c>) and no override was supplied; or the Project is not
    /// <c>Active</c>, which <c>Project.Complete()</c> refuses on its own. <b>The override reaches
    /// only the first.</b> No value of <c>forceOverride</c> completes an <c>OnHold</c> or already
    /// <c>Completed</c> Project.
    /// </para>
    /// <para>
    /// <b>400</b> covers three: <c>forceOverride</c> without a reason (FR-8.6 requires one), a
    /// reason without <c>forceOverride</c> (rejected rather than silently dropped), and
    /// <c>forceOverride</c> when nothing is actually blocking — an override must override
    /// something, and a false justification must not enter the audit trail.
    /// </para>
    /// <para>
    /// <b>The body is optional</b>; omitting it means no override. The Admin's id comes from the
    /// token's subject claim and the Project's from the route (D61).
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        int id,
        [FromBody] CompleteProjectRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new CompleteProjectRequest();

        var project = await completeProjectHandler.HandleAsync(
            new CompleteProjectCommand(
                ProjectId: id,
                ForceOverride: request.ForceOverride,
                Reason: request.Reason,
                CompletedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(project);
    }

    /// <summary>
    /// One Project with the originating context and Invoices Wireframe E1 renders (SRS FR-7.4).
    /// Admin "F", Inspector "R".
    /// </summary>
    /// <remarks>
    /// <b>FR-7.4 is served in full as of Phase 8 Slice 6.</b> The response carries the Project, its
    /// Customer's name, the originating Lead/Inspection/Angebot ids, the "Invoiced"/"Remaining"
    /// figures and the Project's Invoices. The two figures follow Slice 3's rules exactly —
    /// <c>Void</c> excluded and nothing else, never clamped, so a negative <c>remaining</c> is
    /// BR-3's warning. <b>Voided Invoices still appear in the list</b> (BR-9); they are absent from
    /// the arithmetic only. All of it is Inspector-readable and unscoped, and none of it confers
    /// any Invoice-management permission.
    /// </remarks>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProjectDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var project = await getProjectByIdHandler.HandleAsync(
            new GetProjectByIdQuery(id),
            cancellationToken);

        return Ok(project);
    }

    /// <summary>
    /// A Project's invoice balance (BR-3, Sequence Diagram §8, Wireframes E1/E2). Admin "F",
    /// Inspector "R".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is Project financial-summary data, not an Invoice-management action</b>, which is why
    /// it sits on this controller rather than <c>InvoicesController</c>. `PermissionMatrix.md` §5
    /// grants it Admin <c>F</c> / Inspector <c>R</c> — the same grant as the Project detail read
    /// directly above, read-only and **unscoped**, so there is no <c>IOwnershipValidator</c> call
    /// and no per-Inspector filtering. It confers no permission over Invoices themselves: create,
    /// send, mark-paid and void all remain Admin-only on <c>InvoicesController</c>.
    /// </para>
    /// <para>
    /// <b><c>remaining</c> may be negative, and that is the point.</b> BR-3 warns rather than
    /// blocks, so a Project invoiced beyond its agreed total reports a negative remainder — that
    /// value *is* the warning. It is never clamped, and there is no separate warning flag.
    /// <c>alreadyInvoiced</c> excludes <c>Void</c> invoices (StateMachine.md §3.3) and nothing else.
    /// </para>
    /// </remarks>
    [HttpGet("{id:int}/invoice-balance")]
    [ProducesResponseType<ProjectInvoiceBalanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoiceBalance(int id, CancellationToken cancellationToken)
    {
        var balance = await getInvoiceBalanceHandler.HandleAsync(
            new GetProjectInvoiceBalanceQuery(id),
            cancellationToken);

        return Ok(balance);
    }

    /// <summary>The authenticated caller's user id, from the token's subject claim (D61).</summary>
    private int CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(subject, out var userId)
            ? userId
            : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
    }
}
