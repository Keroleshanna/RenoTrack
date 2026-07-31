using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RenoTrack.Api.Auth.Dtos;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// Dashboard authentication (Architecture.md §7.1, SRS FR-10.1/FR-10.3).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the one controller that does not dispatch to an Application-layer command handler.
/// Authentication has no aggregate, no Domain invariant, and no audit milestone; routing it through
/// the CQRS pipeline would require inventing an abstraction over Identity purely so the Application
/// layer could participate in something it has no business rules for. See D60 — this is an
/// intentional exception, not an inconsistency to be tidied up later.
/// </para>
/// <para>
/// <b>Every failure returns the same 401 with the same message.</b> Unknown email, wrong password,
/// inactive account, and locked-out account are indistinguishable to the caller, because
/// distinguishing them turns this endpoint into an account-enumeration oracle. This is a
/// deliberate exception to D59's "mapped exceptions carry a useful message" policy: here the
/// unhelpfulness is the feature. Do not "improve" these messages.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            // Logged (not returned) so a real brute-force attempt is visible in logs even though
            // the response says nothing.
            logger.LogWarning("Login failed: no user for the supplied email.");
            return Unauthorized(InvalidCredentialsMessage);
        }

        // Checked before the password so a locked account cannot be probed for password
        // correctness by timing or by any future difference in handling.
        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Login failed: user {UserId} is locked out.", user.Id);
            return Unauthorized(InvalidCredentialsMessage);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // SRS FR-10.3 (rate-limit failed logins). CheckPasswordAsync does not touch the
            // lockout counters by itself — only SignInManager does, and AddIdentityCore
            // deliberately does not register SignInManager (D54, avoiding cookie-auth defaults this
            // JWT API never uses). So the counter is incremented explicitly here.
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Login failed: incorrect password for user {UserId}.", user.Id);
            return Unauthorized(InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login failed: user {UserId} is inactive.", user.Id);
            return Unauthorized(InvalidCredentialsMessage);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var tokens = await tokenService.IssueAsync(user.Id, user.Email!, user.Name, roles, cancellationToken);

        return Ok(ToResponse(tokens, user, roles));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokens = await tokenService.RotateAsync(request.RefreshToken, cancellationToken);

        if (tokens is null)
        {
            // Unknown, expired, revoked, or belonging to a now-inactive user — all one answer, for
            // the same non-disclosure reason as login.
            return Unauthorized("Invalid refresh token.");
        }

        var user = await userManager.FindByIdAsync(tokens.UserId.ToString());

        // RotateAsync only returns a pair after confirming the user exists and is active, so this
        // is unreachable in practice; handled rather than asserted because returning 401 is a safe
        // response to an impossible state.
        if (user is null)
        {
            return Unauthorized("Invalid refresh token.");
        }

        var roles = await userManager.GetRolesAsync(user);

        return Ok(ToResponse(tokens, user, roles));
    }

    private static AuthResponse ToResponse(TokenPair tokens, ApplicationUser user, IEnumerable<string> roles) =>
        new(
            tokens.AccessToken,
            tokens.ExpiresAt,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt,
            user.Id,
            user.Name,
            user.Email!,
            roles.FirstOrDefault() ?? string.Empty);
}
