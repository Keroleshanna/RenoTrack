using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RenoTrack.Api.RateLimiting;
using RenoTrack.Api.Public.Dtos;
using RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Invoices.Queries.GetInvoiceDocument;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceByToken;
using RenoTrack.Application.Invoices.Queries.GetPublicInvoiceDocumentByToken;
using RenoTrack.Application.Common;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// The customer-facing token-link surface (Architecture.md §7.2, SRS FR-6.2). Every action here is
/// reachable by anyone holding the link and no one else — there is no account, no login, and no
/// principal of any kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate controller rather than more actions on <c>AngeboteController</c>.</b> That
/// controller is <c>[Authorize(Roles = Admin,Inspector)]</c> at class level, so an anonymous action
/// there would be a single <c>[AllowAnonymous]</c> attribute away from every authenticated action
/// around it — one careless copy-paste from exposing a staff endpoint. Keeping the anonymous
/// surface in its own file under its own route prefix makes "what can the public reach?" answerable
/// by opening one file, which is the reviewability Architecture.md §7.2 and
/// <c>PROJECT_ROADMAP.md</c>'s Phase 6 rationale both ask for.
/// </para>
/// <para>
/// <b><c>[AllowAnonymous]</c> is declared at class level here, the one deliberate inversion of
/// CLAUDE.md §22's default.</b> The rule exists because a forgotten <c>[Authorize]</c> silently
/// exposes an endpoint while a forgotten <c>[AllowAnonymous]</c> merely fails closed. On this
/// controller every action is anonymous by definition, so the safe-by-omission direction is
/// reversed: an action added here without the attribute would be unreachable by the only audience
/// it exists for. The controller's whole identity is "the public surface", so it is declared once,
/// visibly, at the top.
/// </para>
/// <para>
/// <b>Rate-limited as one route group</b> (Architecture.md §12, D65). The class-level
/// <c>[EnableRateLimiting]</c> puts every action here in the one <c>public</c> policy and one
/// per-client bucket, so an action added later is limited by default. That includes the invoice
/// document download (Phase 14 Slice 3b, D114), which shares its bucket with the invoice read.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/public")]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
[AllowAnonymous]
public sealed class PublicController(
    IQueryHandler<GetPublicAngebotByTokenQuery, PublicAngebotDto> getPublicAngebotHandler,
    ICommandHandler<RecordAngebotDecisionCommand, PublicAngebotDto> recordDecisionHandler,
    IQueryHandler<GetPublicInvoiceByTokenQuery, PublicInvoiceDto> getPublicInvoiceHandler,
    IQueryHandler<GetPublicInvoiceDocumentByTokenQuery, InvoiceDocumentFile> getPublicInvoiceDocumentHandler) : ControllerBase
{
    /// <summary>
    /// The read-only Angebot behind a token link (SRS FR-6.2, Wireframe A3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Still readable after a decision</b> — BR-4 restricts single use to state-changing actions
    /// and says viewing remains allowed, so a customer can re-read what they agreed to. The
    /// response's <c>decision</c>/<c>decisionAt</c> tell the page whether to offer the buttons.
    /// </para>
    /// <para>
    /// 404 covers both an unknown token and a token belonging to something other than an Angebot,
    /// deliberately indistinguishably. 410 means the link genuinely expired — a distinction that
    /// costs nothing to disclose when the secret is 256 bits of CSPRNG output, and that spares a
    /// legitimate customer hunting for a mistyped URL.
    /// </para>
    /// </remarks>
    [HttpGet("angebote/{token}")]
    [ProducesResponseType<PublicAngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetAngebot(string token, CancellationToken cancellationToken)
    {
        var angebot = await getPublicAngebotHandler.HandleAsync(
            new GetPublicAngebotByTokenQuery(token),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// The customer approves or rejects (SRS FR-6.3/FR-6.5, Wireframe A3's two buttons).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consuming the link, recording the Angebot's decision and moving the Lead to <c>Won</c> or
    /// <c>Lost</c> all commit together — StateMachine.md §5 requires the Lead transition to happen
    /// inside this handler's transaction, and this is the <b>only</b> path to those two states.
    /// </para>
    /// <para>
    /// <b>409 for a link that has already decided</b>, per BR-4's single-use rule — while the same
    /// link stays readable through the GET above, which is exactly what BR-4 distinguishes. 410 is
    /// reserved for genuine expiry, 404 for an unknown or non-Angebot token.
    /// </para>
    /// <para>
    /// Returns the updated public view, so the page can render the recorded outcome without a
    /// second round trip. <b>That view carries no rejection reason</b> (D98): the customer submits
    /// it, staff read it back on the Angebot detail endpoint, and the public contract is not
    /// widened to echo customer-authored free text through a credential anyone holding a forwarded
    /// email can present.
    /// </para>
    /// <para>
    /// <b>400 for a reason sent with an approval</b>, and for one over 1000 characters — shape
    /// rules, refused rather than dropped (D98, following K-4/D67).
    /// </para>
    /// </remarks>
    [HttpPost("angebote/{token}/decision")]
    [ProducesResponseType<PublicAngebotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> RecordDecision(
        string token,
        RecordDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var angebot = await recordDecisionHandler.HandleAsync(
            new RecordAngebotDecisionCommand(token, request.Decision, request.Reason),
            cancellationToken);

        return Ok(angebot);
    }

    /// <summary>
    /// The read-only Invoice behind a token link (SRS FR-8.3, Wireframe A4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read-only is the whole surface.</b> `PermissionMatrix.md` §7 grants the customer "View
    /// Invoice via token link" and nothing else — there is no invoice decision action, so unlike the
    /// Angebot link this one is never consumed and <c>UsedAt</c> is never set on it. BR-4 mentions
    /// "an Invoice's decision-type action" hypothetically; none is documented and none is invented.
    /// </para>
    /// <para>
    /// 404 covers both an unknown token and a token belonging to something other than an Invoice,
    /// deliberately indistinguishably. 410 means the link genuinely expired.
    /// </para>
    /// <para>
    /// <b>Bank details are absent</b>, though Wireframe A4 shows them: no document defines where the
    /// company's IBAN/BIC live (G-5). A4's "[ Download PDF ]" is the action below, and
    /// <c>documentAvailable</c> tells the page whether to offer it (D114). The VAT is still a total:
    /// the per-rate lines (D111) are printed on the PDF, and this read was deliberately not widened.
    /// </para>
    /// </remarks>
    [HttpGet("invoices/{token}")]
    [ProducesResponseType<PublicInvoiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetInvoice(string token, CancellationToken cancellationToken)
    {
        var invoice = await getPublicInvoiceHandler.HandleAsync(
            new GetPublicInvoiceByTokenQuery(token),
            cancellationToken);

        return Ok(invoice);
    }

    /// <summary>
    /// The archived PDF of the Invoice behind a token link (Phase 14 Slice 3b, D114, Wireframe A4's
    /// "[ Download PDF ]").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It serves the archive, byte for byte, and never renders (BR-15).</b> This is the same file
    /// the Admin downloads and the email carried as its attachment. Status does not matter: a
    /// <c>Paid</c> or <c>Void</c> invoice's document is still what was issued.
    /// </para>
    /// <para>
    /// <b>Whoever holds the link can download the PDF</b>, including the customer's address
    /// printed on it. That is the bearer-link model (Architecture.md §7.2), approved for this slice
    /// and recorded in PermissionMatrix.md §7.
    /// </para>
    /// <para>
    /// 404 for an unknown or non-Invoice token, indistinguishably and with the read's own text; 410
    /// for expiry; 409 when the Invoice has no archived document (sent before Slice 3a). A missing
    /// file is a 500 with no detail. The link is never consumed.
    /// </para>
    /// <para>
    /// The response headers are the Admin download's: an attachment named by
    /// <c>InvoiceDocumentFileName</c>, <c>Cache-Control: no-store</c> so no shared cache keeps a
    /// legal document with personal data, and <c>nosniff</c>.
    /// </para>
    /// </remarks>
    [HttpGet("invoices/{token}/document")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, InvoiceDocumentFileName.PdfContentType)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetInvoiceDocument(string token, CancellationToken cancellationToken)
    {
        var document = await getPublicInvoiceDocumentHandler.HandleAsync(
            new GetPublicInvoiceDocumentByTokenQuery(token),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(document.Content, InvoiceDocumentFileName.PdfContentType, document.FileName);
    }
}
