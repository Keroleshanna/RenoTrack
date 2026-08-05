using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Angebote.Dtos;
using RenoTrack.Application.Angebote.Commands.AddAngebotItem;
using RenoTrack.Application.Angebote.Commands.AddAngebotSection;
using RenoTrack.Application.Angebote.Commands.ApproveAngebot;
using RenoTrack.Application.Angebote.Commands.CreateAngebot;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;
using RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;
using RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetAngebotById;
using RenoTrack.Application.Angebote.Queries.GetAngebotReviewComments;
using RenoTrack.Application.Angebote.Queries.GetLeadAngebote;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Angebot builder endpoints (Architecture.md §5.2, PermissionMatrix.md §3–4).
/// </summary>
/// <remarks>
/// <para>
/// The creation route nests under Leads but lives here, following <c>InspectionsController</c>'s
/// precedent: cohesion by resource beats cohesion by URL prefix, and splitting one Angebot action
/// onto <c>LeadsController</c> would give that controller a dependency it has no other use for.
/// </para>
/// <para>
/// <b>Building is Inspector-only and ownership-scoped; reviewing is Admin-only and unscoped.</b>
/// <c>PermissionMatrix.md</c> §3 marks Admin as "R" for a draft in progress — they may view one but
/// never edit it, which keeps authorship and accountability clean; if an Admin wants a change they
/// use Request Changes during review. §4 then marks approve and request-changes "F", so those carry
/// no ownership check at all: any Admin may act on any Angebot, which is the point of the review
/// gate (BR-1). That asymmetry is deliberate — see CLAUDE.md §16.
/// </para>
/// <para>
/// There is deliberately <b>no endpoint returning a <c>ChangesRequested</c> Angebot to
/// <c>Draft</c></b>. StateMachine.md §2.3 states that transition happens "the moment editing
/// resumes", and <c>Angebot.EnsureEditable()</c> already implements exactly that — adding a
/// "reopen" action would invent a transition the documents do not have.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class AngeboteController(
    ICommandHandler<CreateAngebotCommand, AngebotDto> createAngebotHandler,
    ICommandHandler<AddAngebotSectionCommand, SectionDto> addSectionHandler,
    ICommandHandler<AddAngebotItemCommand, AddAngebotItemResult> addItemHandler,
    ICommandHandler<RemoveAngebotSectionCommand, AngebotSummaryDto> removeSectionHandler,
    ICommandHandler<RemoveAngebotItemCommand, AngebotSummaryDto> removeItemHandler,
    IQueryHandler<GetAngebotByIdQuery, AngebotDetailDto> getAngebotByIdHandler,
    IQueryHandler<GetLeadAngeboteQuery, IReadOnlyList<AngebotDto>> getLeadAngeboteHandler,
    ICommandHandler<SubmitAngebotForReviewCommand, AngebotDto> submitForReviewHandler,
    ICommandHandler<ApproveAngebotCommand, AngebotDto> approveHandler,
    ICommandHandler<RequestAngebotChangesCommand, AngebotDto> requestChangesHandler,
    IQueryHandler<GetAngebotReviewCommentsQuery, IReadOnlyList<AngebotReviewCommentDto>> getReviewCommentsHandler) : ControllerBase
{
    /// <summary>
    /// Creates a Draft Angebot for a Lead (SRS FR-4.1). Owning Inspector only.
    /// </summary>
    /// <remarks>
    /// Two guards live below this method and are deliberately not duplicated here: the handler's
    /// <c>IOwnershipValidator</c> call (PermissionMatrix.md §3 marks this "S"), and StateMachine.md
    /// §2.4's "only one non-terminal Angebot per Lead", which surfaces as 409 via
    /// <c>ConflictException</c>. The Lead's own guard rejects creating an Angebot before the
    /// Inspection is done.
    /// </remarks>
    [HttpPost("/api/v1/leads/{leadId:int}/angebote")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        int leadId,
        CreateAngebotRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await createAngebotHandler.HandleAsync(
            new CreateAngebotCommand(leadId, request.InspectionId, CreatedByInspectorId: CurrentUserId()),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = angebot.Id }, angebot);
    }

    /// <summary>
    /// The full Angebot — header, sections, items and VAT breakdown. Admin "F", Inspector "S".
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<AngebotDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var angebot = await getAngebotByIdHandler.HandleAsync(
            new GetAngebotByIdQuery(id, RequestingInspectorId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// Every Angebot on one Lead, newest first. Admin "F"; an Inspector sees only their own.
    /// </summary>
    /// <remarks>
    /// Unpaged by design — StateMachine.md §2.4 allows one non-terminal Angebot per Lead, so this is
    /// a short bounded history rather than an open collection like the Lead pipeline. It is still
    /// deterministically ordered.
    /// </remarks>
    [HttpGet("/api/v1/leads/{leadId:int}/angebote")]
    [ProducesResponseType<IReadOnlyList<AngebotDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForLead(int leadId, CancellationToken cancellationToken)
    {
        var angebote = await getLeadAngeboteHandler.HandleAsync(
            new GetLeadAngeboteQuery(leadId, RequestingInspectorId()),
            cancellationToken);

        return Ok(angebote);
    }

    /// <summary>Adds a section to an editable Angebot. Owning Inspector only.</summary>
    [HttpPost("{id:int}/sections")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<SectionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddSection(
        int id,
        AddSectionRequest request,
        CancellationToken cancellationToken)
    {
        var section = await addSectionHandler.HandleAsync(
            new AddAngebotSectionCommand(id, request.Title, request.SortOrder, InspectorId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, section);
    }

    /// <summary>
    /// Adds a line item, from the Catalog or fully custom (SRS FR-4.9). Owning Inspector only.
    /// </summary>
    /// <remarks>
    /// Returns the item together with the Angebot's refreshed totals, because adding a line changes
    /// NetTotal/GrossTotal and a caller would otherwise have to re-read the Angebot to render them.
    /// </remarks>
    [HttpPost("{id:int}/items")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AddAngebotItemResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(
        int id,
        AddItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await addItemHandler.HandleAsync(
            new AddAngebotItemCommand(
                id,
                request.SectionId,
                request.CatalogItemId,
                request.Description,
                request.Specification,
                request.UnitCode,
                request.Quantity,
                request.UnitPrice,
                request.VatRate,
                InspectorId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Removes a section and its items while the Angebot is editable (PermissionMatrix.md §3).
    /// </summary>
    /// <remarks>
    /// Returns 200 with the refreshed totals rather than 204, for the same reason item creation
    /// returns them: removing a section changes NetTotal/GrossTotal, and a bodiless response would
    /// force an extra read to render the result of the caller's own action.
    /// </remarks>
    [HttpDelete("{id:int}/sections/{sectionId:int}")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AngebotSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveSection(
        int id,
        int sectionId,
        CancellationToken cancellationToken)
    {
        var summary = await removeSectionHandler.HandleAsync(
            new RemoveAngebotSectionCommand(id, sectionId, InspectorId: CurrentUserId()),
            cancellationToken);

        return Ok(summary);
    }

    /// <summary>Removes a single line item, leaving its section in place.</summary>
    [HttpDelete("{id:int}/items/{itemId:int}")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AngebotSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(
        int id,
        int itemId,
        CancellationToken cancellationToken)
    {
        var summary = await removeItemHandler.HandleAsync(
            new RemoveAngebotItemCommand(id, itemId, InspectorId: CurrentUserId()),
            cancellationToken);

        return Ok(summary);
    }

    // ---- Internal review loop (SRS FR-5.1–5.4, StateMachine.md §2.3) --------

    /// <summary>
    /// Submits a draft for Admin review (SRS FR-5.1). Owning Inspector only.
    /// </summary>
    /// <remarks>
    /// The "at least one section with at least one item" guard is the aggregate's own and surfaces
    /// as 409 — the controller checks nothing. Submitting from any state other than <c>Draft</c>
    /// fails the same way.
    /// </remarks>
    [HttpPost("{id:int}/submit-for-review")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitForReview(int id, CancellationToken cancellationToken)
    {
        var angebot = await submitForReviewHandler.HandleAsync(
            new SubmitAngebotForReviewCommand(id, InspectorId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// Approves an Angebot internally (SRS FR-5.2, BR-1). Admin only.
    /// </summary>
    /// <remarks>
    /// <b>No ownership check, deliberately.</b> PermissionMatrix.md §4 marks this "F": any
    /// authenticated Admin may approve any Angebot, which is the entire point of the internal review
    /// gate. Calling <c>IOwnershipValidator</c> here would be a semantic error, not merely redundant
    /// (CLAUDE.md §16). This is where Phase 5 ends — <c>ApprovedInternally</c> is what Phase 6's
    /// send/token-link work starts from.
    /// </remarks>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        var angebot = await approveHandler.HandleAsync(
            new ApproveAngebotCommand(id, ReviewedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// Returns an Angebot to the Inspector with a comment (SRS FR-5.2/FR-5.3). Admin only, "F".
    /// </summary>
    /// <remarks>
    /// The comment is saved as an <c>AngebotReviewComment</c> and the Inspector is notified, both
    /// inside the handler. The Angebot moves to <c>ChangesRequested</c>, and returns to <c>Draft</c>
    /// by itself the moment the Inspector edits it again — FR-5.3's loop, with no extra endpoint.
    /// </remarks>
    [HttpPost("{id:int}/request-changes")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestChanges(
        int id,
        RequestChangesRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await requestChangesHandler.HandleAsync(
            new RequestAngebotChangesCommand(id, request.Comment, ReviewedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// The review comment history, oldest first (SRS FR-5.4). Admin "F", Inspector "R" for their own.
    /// </summary>
    [HttpGet("{id:int}/review-comments")]
    [ProducesResponseType<IReadOnlyList<AngebotReviewCommentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewComments(int id, CancellationToken cancellationToken)
    {
        var comments = await getReviewCommentsHandler.HandleAsync(
            new GetAngebotReviewCommentsQuery(id, RequestingInspectorId()),
            cancellationToken);

        return Ok(comments);
    }

    /// <summary>The authenticated caller's user id, from the token's subject claim (D61).</summary>
    private int CurrentUserId()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(subject, out var userId)
            ? userId
            : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
    }

    /// <summary>
    /// The caller's own id when they are an Inspector, or <c>null</c> when they are an Admin and
    /// therefore unrestricted ("F" in PermissionMatrix.md §3–4).
    /// </summary>
    /// <remarks>
    /// Written to fail secure, mirroring <c>LeadsController</c>'s helper: <c>null</c> means
    /// unrestricted, so it is only ever returned after positively establishing the Admin role —
    /// never by falling through. Inspector is checked first, so a mis-provisioned dual-role account
    /// is scoped rather than unrestricted. Anything that is neither role is refused outright.
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

        throw new ForbiddenException("This account has no role that grants access to Angebote.");
    }
}
