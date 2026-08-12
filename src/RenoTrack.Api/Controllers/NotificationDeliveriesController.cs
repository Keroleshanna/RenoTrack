using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.Persistence.Entities;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Operational visibility over email notification delivery (`PermissionMatrix.md` §9, Phase 9
/// Slice 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this endpoint exists at all:</b> a committed business operation stays successful when its
/// notification fails (D69), and two of the six senders are anonymous public endpoints — the website
/// contact form and the customer's token-link decision — where no Admin is present in the request to
/// be told anything. Without this list, those failures are visible only in a log file.
/// </para>
/// <para>
/// <b>The second controller that does not dispatch to an Application handler</b>, after
/// <c>AuthController</c>, and for D60's reason rather than a new one: the read has no aggregate, no
/// Domain invariant, no state transition and no audit milestone. The record it reads is
/// Infrastructure's by D69, and its two enums live there, so an Application-side query would have to
/// move them down — creating precisely the notification-persistence abstraction D69 forbids. The
/// boundary this codebase draws is "does this have business rules", not "which layer looks tidier".
/// </para>
/// <para>
/// Thin in the sense <c>CLAUDE.md</c> §22 means it: no <c>if</c> about business state, nothing
/// decided, nothing mapped. The bounds below are request-shape constraints, not business rules, and
/// they are declarative — <c>[ApiController]</c> turns a violation into the same RFC 7807
/// <c>ProblemDetails</c> with a field-keyed <c>errors</c> dictionary that FluentValidation failures
/// produce elsewhere, so the wire contract is uniform even though the mechanism differs.
/// <b>FluentValidation is deliberately not used here:</b> it is an Application-layer package and
/// <c>RenoTrack.Infrastructure</c> does not reference it — adding it to satisfy two integer bounds
/// would be a heavier change than the thing being validated.
/// </para>
/// </remarks>
[ApiController]
// Kebab-case literal rather than [controller], matching CatalogItemsController — the token-based
// default would render this multi-word resource as /api/v1/NotificationDeliveries.
[Route("api/v1/notification-deliveries")]
// Admin only, and only Admin: PermissionMatrix.md §9 marks both notification actions "F" for Admin
// and "—" for Inspector. Receiving a notification (an Inspector does, for "changes requested") and
// administering the delivery system are different concerns. Being a single-role endpoint, there is
// no scope to derive and therefore no fall-through that could fail open — the hazard
// LeadsController.RequestingInspectorId() exists to guard against does not arise here.
[Authorize(Roles = Roles.Admin)]
public sealed class NotificationDeliveriesController(
    INotificationDeliveryQueries notificationDeliveryQueries,
    INotificationRetryService notificationRetryService) : ControllerBase
{
    /// <summary>
    /// Lists notification deliveries, newest first, optionally filtered by status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No ownership check, and that absence is correct</b> rather than an omission — §9's "F"
    /// means role-based authority, so per <c>CLAUDE.md</c> §16 an <c>IOwnershipValidator</c> call
    /// here would be a semantic error, not merely redundant code.
    /// </para>
    /// <para>
    /// Paged because D70 defines <b>no retention policy</b>: this table only ever grows, so an
    /// unbounded list would degrade quietly rather than fail visibly.
    /// </para>
    /// </remarks>
    /// <param name="status">
    /// Optional. Omitted returns every status, <c>Sent</c> included — an Admin needs to confirm a
    /// delivery eventually succeeded, not only that it once failed.
    /// </param>
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationDeliveryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        // No IsInEnum()-equivalent attribute here, and that is a verified absence rather than an
        // oversight. The design review assumed one was needed, because MVC has historically bound an
        // undefined *numeric* value (?status=99) to an enum quite happily — which would answer a
        // nonsense filter with a cheerful empty page instead of a 400. On this runtime it does not:
        // the binder rejects both a non-member name and an undefined ordinal by itself. Confirmed
        // adversarially by removing an [EnumDataType] attribute and watching both cases still return
        // 400, so the attribute was deleted rather than kept as decoration. The behaviour we depend
        // on is pinned by Rejects_an_invalid_status, which covers both shapes — if a future runtime
        // loosens the binder, that test fails and the attribute comes back.
        [FromQuery] NotificationDeliveryStatus? status,
        [FromQuery] [Range(Pagination.FirstPage, int.MaxValue)] int page = Pagination.FirstPage,
        [FromQuery] [Range(1, Pagination.MaxPageSize)] int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await notificationDeliveryQueries.GetPagedAsync(status, page, pageSize, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Re-sends one notification (`PermissionMatrix.md` §9, D70). Phase 9 Slice 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Retries the notification, never the business operation.</b> The message is rebuilt from
    /// currently persisted data and sent; no decision is re-recorded, no Angebot is re-sent as a
    /// business action, and no token is minted. That is structural rather than careful — the retry
    /// path has no access to a command handler at all.
    /// </para>
    /// <para>
    /// <b>No request body, deliberately.</b> The delivery comes from the route and the Admin from
    /// the token (D61); there is nothing a caller could legitimately supply, and accepting anything
    /// would invite them to influence what gets sent.
    /// </para>
    /// <para>
    /// <b>Every refusal is 409</b> (S5-9): already <c>Sent</c>, a claim lost to another Admin, an
    /// expired or already-used token link, a <c>Void</c>/<c>Paid</c> Invoice, and email being
    /// switched off for the deployment. They differ in the <c>detail</c> message, never in status —
    /// each names a state conflict, and one uniform code is what makes the contract predictable.
    /// A <b>delivery</b> failure is not a refusal: the notification stays committed-and-failed, the
    /// row records it, and this still returns 200 describing that outcome.
    /// </para>
    /// </remarks>
    [HttpPost("{id:int}/retry")]
    [ProducesResponseType<NotificationDeliveryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(int id, CancellationToken cancellationToken)
    {
        var delivery = await notificationRetryService.RetryAsync(id, cancellationToken);

        return Ok(delivery);
    }
}
