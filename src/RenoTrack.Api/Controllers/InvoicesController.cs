using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Invoices.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Invoices.Commands.CreateInvoice;
using RenoTrack.Application.Invoices.Commands.SendInvoice;
using RenoTrack.Application.Invoices.Dtos;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Invoice endpoints (Architecture.md §5.2, PermissionMatrix.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Admin only, at class level.</b> `PermissionMatrix.md` §5 marks every Invoice action — create,
/// send, mark paid, void — Admin <c>F</c> / Inspector <c>—</c>. **No <c>IOwnershipValidator</c>
/// anywhere in this feature**, and none should be added: an <c>F</c> action has no ownership rule,
/// so a check here would be a semantic error rather than merely redundant (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>The Project's invoice balance is deliberately *not* here.</b> It lives on
/// <c>ProjectsController</c>, because §5 grants it Admin <c>F</c> / Inspector <c>R</c> as Project
/// financial-summary data. An Inspector reading that summary gains no Invoice permission — every
/// action on this controller stays Admin-only, which is exactly why the two live apart.
/// </para>
/// <para>
/// The creation route nests under <c>projects</c> but lives here, following the precedent
/// <c>ProjectsController</c> set for <c>POST /api/v1/angebote/{id}/convert-to-project</c>: cohesion
/// by resource beats cohesion by URL prefix.
/// </para>
/// <para>
/// <b>There is deliberately no <c>mark-paid</c>, <c>void</c> or read endpoint yet.</b> The Domain
/// carries both remaining transitions (StateMachine.md §3.3); they arrive in Slice 5.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = Roles.Admin)]
public sealed class InvoicesController(
    ICommandHandler<CreateInvoiceCommand, InvoiceDto> createInvoiceHandler,
    ICommandHandler<SendInvoiceCommand, InvoiceDto> sendInvoiceHandler) : ControllerBase
{
    /// <summary>
    /// Creates one Invoice against a Project, splitting the entered gross across the originating
    /// Angebot's VAT rates (SRS FR-8.1/FR-8.2, Sequence Diagram §8, Wireframe E2). Admin only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Exceeding the remaining balance is not an error.</b> BR-3 warns rather than blocks, so an
    /// invoice that takes a Project past its agreed total is accepted and shows up as a negative
    /// <c>remaining</c> on <c>GET /api/v1/projects/{id}/invoice-balance</c>. There is no 409 for it
    /// and no validator maximum.
    /// </para>
    /// <para>
    /// 409 covers exactly two cases: the Project is <c>Completed</c> (StateMachine.md §5 — an
    /// Invoice needs an <c>Active</c>/<c>OnHold</c> Project), and a positive amount requested
    /// against an Angebot whose gross total is zero, where no VAT split can be derived.
    /// </para>
    /// <para>
    /// <b>201 with no <c>Location</c> header</b> — no <c>GET /api/v1/invoices/{id}</c> is documented
    /// anywhere, so there is no target to point at. The same position
    /// <c>POST /api/v1/leads/{leadId}/inspections</c> already occupies; inventing a read endpoint
    /// purely to satisfy the header would be scope this slice did not agree.
    /// </para>
    /// </remarks>
    [HttpPost("/api/v1/projects/{projectId:int}/invoices")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        int projectId,
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await createInvoiceHandler.HandleAsync(
            new CreateInvoiceCommand(
                ProjectId: projectId,
                GrossAmount: request.GrossAmount,
                DueDate: request.DueDate,
                CreatedByAdminId: CurrentUserId()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, invoice);
    }

    /// <summary>
    /// Sends a <c>Draft</c> Invoice to the customer as a token link (SRS FR-8.3, Sequence Diagram
    /// §9). Admin only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **No request body** — the Invoice id comes from the route and the Admin from the token's
    /// subject claim (D61), so there is no value a caller could legitimately supply. In particular
    /// the token and its expiry are generated server-side.
    /// </para>
    /// <para>
    /// 409 when the Invoice is not <c>Draft</c> (already sent, paid or voided) or its gross amount
    /// is zero — StateMachine.md §3.3's guard, raised by the aggregate itself and mapped by the one
    /// exception handler (D59). **No PDF is generated and none is attached**: Sequence Diagram §9
    /// draws one, but that is Phase 14's work (G-4), and FR-8.3 is satisfied by the link.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/send")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(int id, CancellationToken cancellationToken)
    {
        var invoice = await sendInvoiceHandler.HandleAsync(
            new SendInvoiceCommand(id, SentByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(invoice);
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
