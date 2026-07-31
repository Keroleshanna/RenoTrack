using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Leads.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Leads.Commands.CreateLead;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Application.Leads.Queries.GetLeadById;
using RenoTrack.Application.Leads.Queries.GetLeads;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Lead intake and pipeline endpoints (Architecture.md §5.2).
/// </summary>
/// <remarks>
/// <para>
/// <c>[Authorize]</c> at the class level with <c>[AllowAnonymous]</c> opted into per action, per
/// D57: a forgotten <c>[Authorize]</c> silently exposes an endpoint, whereas a forgotten
/// <c>[AllowAnonymous]</c> merely fails closed.
/// </para>
/// <para>
/// The class-level authorization names both roles explicitly rather than accepting any
/// authenticated caller. Only Admin and Inspector exist (Architecture.md §7.2), and an account
/// holding neither has no defined access to Leads — refusing it here means such a request never
/// reaches an action that would otherwise have to decide what to do with it.
/// <c>[AllowAnonymous]</c> on <see cref="Create"/> still overrides this entirely, which is what
/// keeps the public contact form public.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class LeadsController(
    ICommandHandler<CreateLeadCommand, LeadDto> createLeadHandler,
    IQueryHandler<GetLeadByIdQuery, LeadDto> getLeadByIdHandler,
    IQueryHandler<GetLeadsQuery, PagedResult<LeadDto>> getLeadsHandler) : ControllerBase
{
    /// <summary>
    /// Creates a Lead from the public website contact form (SRS FR-1.3, Sequence Diagram §1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anonymous by design — this is the website's contact form, and requiring a token would defeat
    /// its purpose. One of only two anonymous actions in the API, the other being login.
    /// </para>
    /// <para>
    /// Thin by design (CLAUDE.md §22): it validates nothing (the handler's validator does, and the
    /// Slice 2 middleware maps the failure to a field-keyed 400), decides nothing, and audits
    /// nothing (the handler already logs <c>LeadCreated</c> — auditing here would double-log a
    /// business milestone, §10). Its entire job is translating an HTTP request into the command,
    /// supplying the two fields the caller must not control (D61).
    /// </para>
    /// <para>
    /// <b>Deliberately not idempotent.</b> Two identical submissions create two Leads. Silently
    /// de-duplicating (e.g. same email within some window) would be an invented business rule that
    /// no document asks for, and it would discard a genuine second enquiry — a lost customer, where
    /// a duplicate row is merely something an Admin can close.
    /// </para>
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<LeadDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateLeadRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateLeadCommand(
            request.Name,
            request.Phone,
            request.Email,
            // Server-derived, never from the request body — see CreateLeadRequest's remarks and D61.
            Source: LeadSource.Website,
            request.Address,
            request.Notes,
            CreatedByUserId: null);

        var lead = await createLeadHandler.HandleAsync(command, cancellationToken);

        // Now that GetById exists (Slice 6), this carries a real Location header — deferred from
        // Slice 5 precisely because a Location pointing at a route that 404s is worse than none.
        return CreatedAtAction(nameof(GetById), new { id = lead.Id }, lead);
    }

    /// <summary>
    /// Reads one Lead (Wireframe C1). Admin: any Lead. Inspector: only their own assigned Leads,
    /// enforced by <c>IOwnershipValidator</c> inside the handler — 403, not 404, per
    /// <c>PermissionMatrix.md</c> §1's scoped ("S") marking and CLAUDE.md §16.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<LeadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var lead = await getLeadByIdHandler.HandleAsync(
            new GetLeadByIdQuery(id, RequestingInspectorId()),
            cancellationToken);

        return Ok(lead);
    }

    /// <summary>
    /// The Lead pipeline (SRS FR-2.4, Wireframe B2), filterable by status, assigned Inspector, and
    /// creation date range, with pagination per Architecture.md §5.1.
    /// </summary>
    /// <remarks>
    /// <b>An Inspector's scope is forced here, not requested.</b> <c>PermissionMatrix.md</c> §1
    /// states their pipeline is "filtered server-side", so whatever <c>assignedInspectorId</c> an
    /// Inspector supplies is discarded and replaced with their own id — a caller cannot widen their
    /// own visibility by asking. An Admin has full ("F") access, so their filter passes through
    /// untouched. This is D61's rule applied to a read: the value describes *who the caller is*, so
    /// it comes from the token, never the query string.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<LeadDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] LeadStatus? status,
        [FromQuery] int? assignedInspectorId,
        [FromQuery] DateTime? createdFrom,
        [FromQuery] DateTime? createdTo,
        [FromQuery] int page = Pagination.FirstPage,
        [FromQuery] int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var scopedInspectorId = RequestingInspectorId() ?? assignedInspectorId;

        var result = await getLeadsHandler.HandleAsync(
            new GetLeadsQuery(status, scopedInspectorId, createdFrom, createdTo, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The caller's own user id when they are an Inspector, or <c>null</c> when they are an Admin
    /// and therefore unrestricted ("F" in <c>PermissionMatrix.md</c> §1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written to fail secure.</b> <c>null</c> here means "unrestricted", so it must only ever be
    /// returned after positively establishing that the caller is an Admin — never by falling
    /// through. An earlier version returned <c>null</c> for anyone who simply was not an Inspector,
    /// which meant a broken role-claim mapping, a user seeded without a role, a typo in a role name,
    /// or the later addition of a third role would each have silently granted that caller **every
    /// Lead in the system**. Every one of those failure modes pointed the same way: open.
    /// </para>
    /// <para>
    /// Inspector is checked <em>first</em>, so a mis-provisioned account holding both roles is
    /// scoped rather than unrestricted — when two rules could apply, the narrower one wins. Anything
    /// that is neither role has no defined access and is refused outright rather than defaulted.
    /// </para>
    /// <para>
    /// The class-level <c>[Authorize(Roles = ...)]</c> already rejects such a caller at the door, so
    /// the final throw is defence in depth rather than the primary guard — deliberately kept, since
    /// the attribute and this method could drift apart, and the consequence of that drift going
    /// unnoticed is unrestricted data access.
    /// </para>
    /// </remarks>
    private int? RequestingInspectorId()
    {
        if (User.IsInRole(Roles.Inspector))
        {
            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // A validated token always carries a subject, so this is unreachable in practice.
            // Throwing beats returning null, which would promote the Inspector to unrestricted.
            return int.TryParse(subject, out var userId)
                ? userId
                : throw new ForbiddenException("Authenticated principal has no usable subject claim.");
        }

        if (User.IsInRole(Roles.Admin))
        {
            return null;
        }

        throw new ForbiddenException("This account has no role that grants access to Leads.");
    }
}
