using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RenoTrack.Api.Angebote.Dtos;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Application.Angebote.Commands.AddAngebotItem;
using RenoTrack.Application.Angebote.Commands.AddAngebotSection;
using RenoTrack.Application.Angebote.Commands.ApproveAngebot;
using RenoTrack.Application.Angebote.Commands.CreateAngebot;
using RenoTrack.Application.Angebote.Commands.DuplicateAngebot;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;
using RenoTrack.Application.Angebote.Commands.UpdateAngebotItem;
using RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;
using RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;
using RenoTrack.Application.Angebote.Commands.ResendAngebot;
using RenoTrack.Application.Angebote.Commands.ReturnAngebotForCorrection;
using RenoTrack.Application.Angebote.Commands.SendAngebot;
using RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetAngebotById;
using RenoTrack.Application.Angebote.Queries.GetAngebote;
using RenoTrack.Application.Angebote.Queries.GetAngebotReviewComments;
using RenoTrack.Application.Angebote.Queries.GetLeadAngebote;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Enums;

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
/// <b>Building and submitting: owning Inspector "S", any Admin "F" (D115).</b>
/// <c>PermissionMatrix.md</c> §3 originally gave the Admin "R" on a draft, to keep authorship clean.
/// The Product Owner replaced that: an Admin has the Inspector's full operational capability on an
/// editable Angebot. Authorship still never moves — <c>CreatedByInspectorId</c> is untouched — and
/// every Admin builder mutation is audited instead. The Inspector stays ownership-checked in the
/// handler; the Admin, being "F", is not (CLAUDE.md §16). Creating and duplicating stay
/// Inspector-only by decision. §4 marks approve, request changes and return-for-correction "F" too.
/// </para>
/// <para>
/// <b>Every write that can change content or move the Angebot through review carries
/// <c>expectedVersion</c></b> — in the body, or as a query parameter on a <c>DELETE</c> — and a stale
/// one is a 409 (D115). Send, resend and convert do not: the content is frozen by then. Those same
/// writes carry <c>[GenericConflictResponse]</c>, so every 409 they produce — stale version, no
/// longer editable, lost race — publishes one detail that names no id or state.
/// </para>
/// <para>
/// There is deliberately <b>no endpoint returning a <c>ChangesRequested</c> Angebot to
/// <c>Draft</c></b>. StateMachine.md §2.3 states that transition happens "the moment editing
/// resumes", and <c>Angebot.EnsureEditable()</c> already implements exactly that. The one explicit
/// return to <c>Draft</c> is from <c>InReview</c> — <c>return-for-correction</c>, Admin only — which
/// the documents now define (StateMachine.md §2.3, D115).
/// </para>
/// <para>
/// <b>Submitting for review is nevertheless available directly from <c>ChangesRequested</c></b>
/// (Phase 10). That is not a reopen: it is the same <c>SubmitForReview</c> event with a second
/// legal source state, added because an Inspector who reads the Admin's comment and concludes
/// nothing needs changing otherwise has no way to send the quote back — the only workaround being
/// a pointless edit made solely to satisfy a guard. See <c>Angebot.SubmitForReview</c>.
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
    ICommandHandler<UpdateAngebotItemCommand, AngebotSummaryDto> updateItemHandler,
    IQueryHandler<GetAngebotByIdQuery, AngebotDetailDto> getAngebotByIdHandler,
    IQueryHandler<GetAngeboteQuery, PagedResult<AngebotListItemDto>> getAngeboteHandler,
    IQueryHandler<GetLeadAngeboteQuery, IReadOnlyList<AngebotDto>> getLeadAngeboteHandler,
    ICommandHandler<SubmitAngebotForReviewCommand, AngebotDto> submitForReviewHandler,
    ICommandHandler<ApproveAngebotCommand, AngebotDto> approveHandler,
    ICommandHandler<RequestAngebotChangesCommand, AngebotDto> requestChangesHandler,
    IQueryHandler<GetAngebotReviewCommentsQuery, IReadOnlyList<AngebotReviewCommentDto>> getReviewCommentsHandler,
    ICommandHandler<DuplicateAngebotCommand, AngebotDto> duplicateHandler,
    ICommandHandler<SendAngebotCommand, AngebotDto> sendHandler,
    ICommandHandler<ResendAngebotCommand, AngebotDto> resendHandler,
    ICommandHandler<ReturnAngebotForCorrectionCommand, AngebotDto> returnForCorrectionHandler) : ControllerBase
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

    /// <summary>
    /// Every Angebot, filterable by status and paged. Admin "F"; an Inspector sees only their own.
    /// </summary>
    /// <remarks>
    /// The cross-Lead read the Dashboard needs. <c>GetForLead</c> below answers "this Lead's
    /// history" and cannot answer "which quotes are in review" without one request per Lead.
    ///
    /// <b>Scope is taken from the token, never the query string</b> (D61): an Inspector's own id is
    /// forced in, so a caller cannot widen their visibility by asking — the same rule
    /// <c>LeadsController.GetAll</c> applies to the pipeline.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<AngebotListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] AngebotStatus? status,
        [FromQuery] int page = Pagination.FirstPage,
        [FromQuery] int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await getAngeboteHandler.HandleAsync(
            new GetAngeboteQuery(status, RequestingInspectorId(), page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Adds a section to an editable Angebot. Owning Inspector or any Admin (D115).</summary>
    [HttpPost("{id:int}/sections")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
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
            new AddAngebotSectionCommand(
                id,
                request.Title,
                request.SortOrder,
                request.ExpectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, section);
    }

    /// <summary>
    /// Adds a line item, from the Catalog or fully custom (SRS FR-4.9). Owning Inspector or any
    /// Admin (D115).
    /// </summary>
    /// <remarks>
    /// Returns the item together with the Angebot's refreshed totals, because adding a line changes
    /// NetTotal/GrossTotal and a caller would otherwise have to re-read the Angebot to render them.
    /// </remarks>
    [HttpPost("{id:int}/items")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
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
                request.ExpectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Removes a section and its items while the Angebot is editable (PermissionMatrix.md §3).
    /// Owning Inspector or any Admin (D115).
    /// </summary>
    /// <remarks>
    /// Returns 200 with the refreshed totals rather than 204, for the same reason item creation
    /// returns them: removing a section changes NetTotal/GrossTotal, and a bodiless response would
    /// force an extra read to render the result of the caller's own action. <c>expectedVersion</c> is
    /// a required query parameter, because a <c>DELETE</c> carries no body.
    /// </remarks>
    [HttpDelete("{id:int}/sections/{sectionId:int}")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
    [ProducesResponseType<AngebotSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveSection(
        int id,
        int sectionId,
        [FromQuery, BindRequired] int expectedVersion,
        CancellationToken cancellationToken)
    {
        var summary = await removeSectionHandler.HandleAsync(
            new RemoveAngebotSectionCommand(
                id,
                sectionId,
                expectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return Ok(summary);
    }

    /// <summary>
    /// Corrects an existing line (`PermissionMatrix.md` §3, Phase 10). Owning Inspector or any
    /// Admin (D115), while the Angebot is editable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added after QA found that a typo could only be fixed by deleting and re-entering a
    /// line.</b> That is not equivalent: recreating loses the line's identity and with it any
    /// Catalog entry contributed from it (FR-4.10), which is a real cost for a spelling mistake.
    /// </para>
    /// <para>
    /// <c>PUT</c>, because the body carries every editable value and replaces all of them. Returns
    /// the refreshed totals and VAT breakdown, not the line — a quantity or price change moves the
    /// document's money, and that is what the screen must re-render (D81).
    /// </para>
    /// <para>
    /// <b>409</b> once the Angebot leaves <c>Draft</c>/<c>ChangesRequested</c> — the aggregate's own
    /// edit-lock (StateMachine §2.4), not re-checked here. <b>400</b> for an unrecognised unit code
    /// or a non-positive quantity. A Catalog-sourced line is editable too, and keeps its provenance
    /// link: BR-8 makes the copy independent at creation.
    /// </para>
    /// </remarks>
    [HttpPut("{id:int}/items/{itemId:int}")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
    [ProducesResponseType<AngebotSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateItem(
        int id,
        int itemId,
        UpdateAngebotItemRequest request,
        CancellationToken cancellationToken)
    {
        var summary = await updateItemHandler.HandleAsync(
            new UpdateAngebotItemCommand(
                AngebotId: id,
                ItemId: itemId,
                request.Description,
                request.Specification,
                request.UnitCode,
                request.Quantity,
                request.UnitPrice,
                request.VatRate,
                request.ExpectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return Ok(summary);
    }

    /// <summary>
    /// Removes a single line item, leaving its section in place. Owning Inspector or any Admin
    /// (D115); <c>expectedVersion</c> is a required query parameter.
    /// </summary>
    [HttpDelete("{id:int}/items/{itemId:int}")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
    [ProducesResponseType<AngebotSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(
        int id,
        int itemId,
        [FromQuery, BindRequired] int expectedVersion,
        CancellationToken cancellationToken)
    {
        var summary = await removeItemHandler.HandleAsync(
            new RemoveAngebotItemCommand(
                id,
                itemId,
                expectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return Ok(summary);
    }

    /// <summary>
    /// Starts a new Draft on another Lead from an existing Angebot (SRS FR-4.11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ownership rules apply and both are enforced in the handler: the Inspector must own the
    /// Angebot being copied (PermissionMatrix.md §3's "only from Angebote the Inspector has access
    /// to", with "their own" as the documented v1 default) <em>and</em> the target Lead. The
    /// one-active-Angebot rule applies to the target exactly as it does to a fresh creation, so
    /// duplicating cannot become a second route around StateMachine.md §2.4.
    /// </para>
    /// <para>
    /// Duplicating a single section is not offered. FR-4.11 permits it, but no wireframe shows it
    /// and it needs its own endpoint shape — it should arrive with a real caller.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/duplicate")]
    [Authorize(Roles = Roles.Inspector)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Duplicate(
        int id,
        DuplicateAngebotRequest request,
        CancellationToken cancellationToken)
    {
        var duplicate = await duplicateHandler.HandleAsync(
            new DuplicateAngebotCommand(id, request.TargetLeadId, InspectorId: CurrentUserId()),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = duplicate.Id }, duplicate);
    }

    // ---- Internal review loop (SRS FR-5.1–5.4, StateMachine.md §2.3) --------

    /// <summary>
    /// Submits an editable Angebot for Admin review (SRS FR-5.1). Owning Inspector or any Admin
    /// (D115) — an Admin typically after correcting it.
    /// </summary>
    /// <remarks>
    /// The "at least one section with at least one item" guard is the aggregate's own and surfaces
    /// as 409 — the controller checks nothing. Submitting from any state other than <c>Draft</c> or
    /// <c>ChangesRequested</c> (D94) fails the same way. The review email goes out only when an
    /// Inspector submits (SRS FR-9.2).
    /// </remarks>
    [HttpPost("{id:int}/submit-for-review")]
    [GenericConflictResponse]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitForReview(
        int id,
        ExpectedVersionRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await submitForReviewHandler.HandleAsync(
            new SubmitAngebotForReviewCommand(
                id,
                request.ExpectedVersion,
                RequestingInspectorId: RequestingInspectorId(),
                PerformedByUserId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// "Zur Korrektur zurückholen": takes an Angebot out of review so an Admin can correct it
    /// (<c>InReview → Draft</c>, SRS FR-5.5, <b>D115</b>). Admin only, "F".
    /// </summary>
    /// <remarks>
    /// <para>
    /// No comment and no email, unlike Request Changes — that is the point of it. The corrected
    /// Angebot must then be submitted and approved again, so the review gate (BR-1) still applies.
    /// </para>
    /// <para>
    /// <b>No ownership check</b> (CLAUDE.md §16). An Inspector is refused by the role gate with an
    /// empty 403. <b>409</b> when the Angebot is not <c>InReview</c>, or <c>expectedVersion</c> is
    /// stale.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/return-for-correction")]
    [GenericConflictResponse]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReturnForCorrection(
        int id,
        ExpectedVersionRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await returnForCorrectionHandler.HandleAsync(
            new ReturnAngebotForCorrectionCommand(id, request.ExpectedVersion, PerformedByAdminId: CurrentUserId()),
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
    [GenericConflictResponse]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        int id,
        ExpectedVersionRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await approveHandler.HandleAsync(
            new ApproveAngebotCommand(id, request.ExpectedVersion, ReviewedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// Sends an internally approved Angebot to the customer (SRS FR-6.1). Admin only, "F".
    /// </summary>
    /// <remarks>
    /// Issues the single-use token link and emails it to the Lead. The Angebot moves
    /// <c>ApprovedInternally → Sent</c> and the Lead <c>AngebotInProgress → AngebotSent</c>, both
    /// committed with the token row in one transaction.
    ///
    /// <b>No request body and no ownership check.</b> Every value is server-derived — the id from
    /// the route, the Admin from the JWT (D61) — and PermissionMatrix.md §4 marks this "F", so any
    /// authenticated Admin may send any Angebot (CLAUDE.md §16).
    ///
    /// <b>No Location header</b>, and deliberately: the resource this creates is a token link,
    /// whose whole security model is that its URL is known only to the customer who receives it by
    /// email. Returning it to the sender would put a live customer credential in a response header
    /// and in every intermediary's logs.
    /// </remarks>
    [HttpPost("{id:int}/send")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(int id, CancellationToken cancellationToken)
    {
        var angebot = await sendHandler.HandleAsync(
            new SendAngebotCommand(id, SentByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// Re-issues the customer's token link (SRS FR-6.1a, <b>D99</b>). Admin only, "F".
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a lost email, a link that lapsed before the Lead answered, or a corrected address. The
    /// previous link is superseded — expired, never deleted and never marked used — in the same
    /// transaction that creates its replacement, so a Lead never holds two working links.
    /// </para>
    /// <para>
    /// <b>The response carries no token</b>, for the reason <c>Send</c> records above: the
    /// credential's whole security model is that only the customer who receives the email knows it.
    /// </para>
    /// <para>
    /// <b>409 covers three distinct refusals, all of them "the state says no"</b>: the Angebot is
    /// not <c>Sent</c>; the existing link already carried the customer's decision (BR-4); or a
    /// concurrent re-issue or decision won the race — in which case nothing at all was written,
    /// because EF rolls the losing batch back and takes its staged replacement with it.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/resend")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resend(int id, CancellationToken cancellationToken)
    {
        var angebot = await resendHandler.HandleAsync(
            new ResendAngebotCommand(id, ResentByAdminId: CurrentUserId()),
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
    [GenericConflictResponse]
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
            new RequestAngebotChangesCommand(id, request.Comment, request.ExpectedVersion, ReviewedByAdminId: CurrentUserId()),
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
