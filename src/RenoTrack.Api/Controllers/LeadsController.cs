using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Leads.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Leads.Commands.CreateLead;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Lead intake and pipeline endpoints (Architecture.md §5.2).
/// </summary>
/// <remarks>
/// <c>[Authorize]</c> at the class level with <c>[AllowAnonymous]</c> opted into per action, per
/// D57: a forgotten <c>[Authorize]</c> silently exposes an endpoint, whereas a forgotten
/// <c>[AllowAnonymous]</c> merely fails closed.
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public sealed class LeadsController(
    ICommandHandler<CreateLeadCommand, LeadDto> createLeadHandler) : ControllerBase
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
    /// supplying the two fields the caller must not control.
    /// </para>
    /// <para>
    /// <b>Deliberately not idempotent.</b> Two identical submissions create two Leads. Silently
    /// de-duplicating (e.g. same email within some window) would be an invented business rule that
    /// no document asks for, and it would discard a genuine second enquiry — a lost customer, where
    /// a duplicate row is merely something an Admin can close. If duplicate-submission noise ever
    /// becomes a real complaint, the answer is a documented business rule plus a merge/close
    /// workflow, not silent server-side de-duplication.
    /// </para>
    /// <para>
    /// Returns 201 without a <c>Location</c> header: <c>GET /api/v1/leads/{id}</c> does not exist
    /// until Slice 6, and a <c>Location</c> pointing at a 404 would be worse than none. Add it when
    /// the target route genuinely exists.
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

        return StatusCode(StatusCodes.Status201Created, lead);
    }
}
