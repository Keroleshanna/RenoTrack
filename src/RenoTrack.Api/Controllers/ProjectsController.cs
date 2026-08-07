using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;
using RenoTrack.Application.Projects.Dtos;
using RenoTrack.Application.Projects.Queries.GetProjectById;

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
/// <b>There is deliberately no endpoint for <c>PutOnHold</c>, <c>Resume</c> or <c>Complete</c>.</b>
/// The Domain carries all three (StateMachine.md §4.3) but `PROJECT_ROADMAP.md` places
/// <c>CompleteProjectCommand</c> in Phase 8, where its "all Invoices Paid or Void" guard and
/// FR-8.6 override can actually be enforced; on-hold/resume are assigned to no phase at all. Adding
/// any of them here would be scope this phase did not agree.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class ProjectsController(
    ICommandHandler<ConvertAngebotToProjectCommand, ProjectDto> convertHandler,
    IQueryHandler<GetProjectByIdQuery, ProjectDetailDto> getProjectByIdHandler) : ControllerBase
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
    /// One Project with the originating context Wireframe E1 renders (SRS FR-7.4). Admin "F",
    /// Inspector "R".
    /// </summary>
    /// <remarks>
    /// **FR-7.4's Invoice portion is not served here and is deferred to Phase 8** — Invoices do not
    /// exist yet. The response carries the Project, its Customer's name, and the originating Lead,
    /// Inspection and Angebot ids; it does not carry the invoice list, "Invoiced" or "Remaining".
    /// A documented gap, not an oversight.
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

    /// <summary>The authenticated caller's user id, from the token's subject claim (D61).</summary>
    private int CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(subject, out var userId)
            ? userId
            : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
    }
}
