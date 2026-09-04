using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Invoices.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Enums;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Invoices.Commands.CreateInvoice;
using RenoTrack.Application.Invoices.Commands.RecordPayment;
using RenoTrack.Application.Invoices.Commands.SendInvoice;
using RenoTrack.Application.Invoices.Commands.VoidInvoice;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Application.Invoices.Queries.GetInvoices;
using RenoTrack.Application.Invoices.Queries.GetReceivables;

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
/// <b>Phase 10 added two reads — a list and a receivables aggregate — but still no
/// <c>GET /api/v1/invoices/{id}</c>.</b> That remains undefined by any document, which is why
/// creation still returns 201 without a <c>Location</c> header. The list exists because the company
/// previously had no way to see its own receivables at all: the only Invoice read in the system was
/// the anonymous token-link page, which serves one customer's single invoice.
/// </para>
/// <para>
/// The one remaining Invoice transition, <c>Sent → Overdue</c>, exists in the Domain but has no
/// endpoint by explicit decision — nothing schedules it, and inventing a trigger for it was
/// rejected. <b>Phase 10 does not add one either:</b> the Dashboard derives "overdue" as
/// <c>Status == Sent &amp;&amp; DueDate &lt; asOf</c> at read time, so the figure is correct without
/// any background process ever writing that status.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = Roles.Admin)]
public sealed class InvoicesController(
    ICommandHandler<CreateInvoiceCommand, InvoiceDto> createInvoiceHandler,
    ICommandHandler<SendInvoiceCommand, InvoiceDto> sendInvoiceHandler,
    ICommandHandler<RecordPaymentCommand, InvoiceDto> recordPaymentHandler,
    ICommandHandler<VoidInvoiceCommand, InvoiceDto> voidInvoiceHandler,
    IQueryHandler<GetInvoicesQuery, PagedResult<InvoiceListItemDto>> getInvoicesHandler,
    IQueryHandler<GetReceivablesQuery, ReceivablesSummaryDto> getReceivablesHandler) : ControllerBase
{
    /// <summary>
    /// The Invoice list, filterable by status, Project and due date. Admin only (§5).
    /// </summary>
    /// <remarks>
    /// Phase 10's read. Before it the only Invoice read in the system was the anonymous token-link
    /// page, so the company could not see its own receivables at all.
    ///
    /// Ordered by due date ascending — an invoice list is a chase list, and the most overdue money
    /// is the most urgent.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<InvoiceListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] InvoiceStatus? status,
        [FromQuery] int? projectId,
        [FromQuery] DateTime? dueBefore,
        [FromQuery] int page = Pagination.FirstPage,
        [FromQuery] int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await getInvoicesHandler.HandleAsync(
            new GetInvoicesQuery(status, projectId, dueBefore, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Invoiced / paid / open / overdue across the whole invoice book. Admin only (§5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate endpoint rather than a field on the list, because it aggregates the **entire** set
    /// while the list returns one page. Attaching a whole-book total to a paged response would put
    /// two different scopes in one payload and invite a client to believe the page explained it.
    /// </para>
    /// <para>
    /// <c>asOf</c> defaults to today on the server only when the caller omits it. Passing it
    /// explicitly is preferred and is what the Dashboard does, so the client's own notion of "today"
    /// decides what counts as overdue rather than the server's timezone.
    /// </para>
    /// </remarks>
    [HttpGet("receivables")]
    [ProducesResponseType<ReceivablesSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReceivables(
        [FromQuery] DateTime? asOf,
        CancellationToken cancellationToken = default)
    {
        var result = await getReceivablesHandler.HandleAsync(
            new GetReceivablesQuery(asOf ?? DateTime.UtcNow.Date),
            cancellationToken);

        return Ok(result);
    }

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

    /// <summary>
    /// Records the Admin's manual confirmation that an Invoice was paid (SRS FR-8.4, Sequence
    /// Diagram §9, Wireframe E3). Admin only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Body is <c>{ paidAt, method }</c> — **no amount**. Phase 8 records full payment only, so the
    /// <c>Payment</c> always carries the Invoice's own gross; FR-8.4, §9 and E3 all offer no amount
    /// to supply. The recording Admin comes from the token (D61).
    /// </para>
    /// <para>
    /// 409 when the Invoice is not <c>Sent</c> or <c>Overdue</c> — which is also what makes a
    /// duplicate confirmation impossible rather than merely discouraged, since <c>Paid</c> is
    /// terminal (StateMachine.md §3.2).
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/mark-paid")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkPaid(
        int id,
        RecordPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await recordPaymentHandler.HandleAsync(
            new RecordPaymentCommand(
                InvoiceId: id,
                PaidAt: request.PaidAt,
                Method: request.Method,
                RecordedByAdminId: CurrentUserId()),
            cancellationToken);

        return Ok(invoice);
    }

    /// <summary>
    /// Cancels an Invoice (PermissionMatrix.md §5, StateMachine.md §3.3). Admin only, and a reason
    /// is always required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>BR-9: the row and its number survive.</b> This is a status change, never a delete — no
    /// code path anywhere in this project removes an Invoice, and the number can never be reused.
    /// </para>
    /// <para>
    /// 409 when the Invoice is already <c>Paid</c> or <c>Void</c> (both terminal). 400 when no
    /// reason is supplied — required from every source state including <c>Draft</c>, which is the
    /// reconciliation Slice 1 made to §3.3's blank guard cell.
    /// </para>
    /// <para>
    /// A voided Invoice leaves the "remaining balance" math immediately (§3.3), with no work here:
    /// the balance query already excludes <c>Void</c>. Its token link stays readable and now reports
    /// <c>Void</c> to the customer.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/void")]
    [ProducesResponseType<InvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void(
        int id,
        VoidInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await voidInvoiceHandler.HandleAsync(
            new VoidInvoiceCommand(
                InvoiceId: id,
                Reason: request.Reason,
                VoidedByAdminId: CurrentUserId()),
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
