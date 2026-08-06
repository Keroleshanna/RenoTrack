using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Public.Dtos;
using RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;
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
/// <b>Not yet rate-limited.</b> Architecture.md §12 requires abuse protection on
/// <c>/api/v1/public/*</c>; it arrives in Slice 4 together with the decision endpoint, so that the
/// limiter is configured once for the whole route group rather than twice.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/public")]
[AllowAnonymous]
public sealed class PublicController(
    IQueryHandler<GetPublicAngebotByTokenQuery, PublicAngebotDto> getPublicAngebotHandler,
    ICommandHandler<RecordAngebotDecisionCommand, PublicAngebotDto> recordDecisionHandler) : ControllerBase
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
    /// second round trip.
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
            new RecordAngebotDecisionCommand(token, request.Decision),
            cancellationToken);

        return Ok(angebot);
    }
}
