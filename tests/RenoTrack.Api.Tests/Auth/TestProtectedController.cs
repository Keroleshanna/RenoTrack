using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RenoTrack.Api.Tests.Auth;

/// <summary>
/// EXISTS SOLELY TO GIVE THE AUTHENTICATION TESTS A PROTECTED ENDPOINT TO CALL.
/// IT IS NEVER PART OF THE PRODUCTION APPLICATION.
/// </summary>
/// <remarks>
/// <para>
/// Same rationale and mechanism as <c>TestErrorsController</c> (Slice 2): defined in the test
/// assembly, so shipping it is structurally impossible rather than merely unlikely, and reaching
/// the application only via an MVC <c>ApplicationPart</c> registered by the test class. Routed
/// under <c>api/test-protected</c>, deliberately not <c>api/v1/...</c>, so it can never collide
/// with a real route or imply membership in the versioned public surface (D57).
/// </para>
/// <para>
/// Why it is needed: as of Slice 4 the only endpoints that exist are <c>/auth/login</c> and
/// <c>/auth/refresh</c>, both <c>[AllowAnonymous]</c>. Nothing in the application yet rejects a
/// bad access token, so "invalid signature" and "expired token" have nothing to be tested against.
/// This controller supplies that surface until real protected endpoints arrive in Slice 5 onward.
/// It should be deleted once those endpoints make it redundant.
/// </para>
/// </remarks>
[ApiController]
[Route("api/test-protected")]
[Authorize]
public sealed class TestProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { user = User.Identity?.Name });

    [HttpGet("admin-only")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly() => Ok();
}
