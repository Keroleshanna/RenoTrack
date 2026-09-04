using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// The staff directory — names for the user ids every business DTO carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consumes an Infrastructure query directly, bypassing the CQRS pipeline.</b> That is D60's
/// exception applied on D60's own test, the third time this codebase has taken it (after
/// <c>AuthController</c> and <c>NotificationDeliveriesController</c>): Identity is Infrastructure-only
/// by D53, there is no aggregate, no Domain invariant, no state transition and no audit milestone.
/// Routing it through an Application query would require an abstraction existing purely so a layer
/// with no business rules about staff could appear to own them. See <see cref="IUserDirectoryQueries"/>.
/// </para>
/// <para>
/// <b>Both roles may read it, and neither may write it.</b> `PermissionMatrix.md` §8 makes account
/// administration Admin-only, and **this controller offers none** — no create, no deactivate, no
/// role change. It exposes a name and a role, which is strictly less than what any Angebot or
/// Inspection already reveals about who did the work. An Inspector needs it for the same reason an
/// Admin does: to see who a visit is assigned to.
/// </para>
/// <para>
/// <b>No email, no password state, no lockout state, no created-at.</b> The screens need a name; the
/// rest is account data with no presentation use, and returning it would widen the surface for
/// nothing.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Inspector}")]
public sealed class UsersController(IUserDirectoryQueries userDirectory) : ControllerBase
{
    /// <summary>
    /// Staff members, optionally narrowed to one role. Active accounts only unless asked otherwise.
    /// </summary>
    /// <remarks>
    /// Unpaged: this is a company's internal staff list, not an open collection, and an assignment
    /// dropdown needs the whole set to be useful.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStaff(
        [FromQuery] string? role,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var staff = await userDirectory.GetStaffAsync(role, activeOnly, cancellationToken);

        return Ok(staff);
    }
}
