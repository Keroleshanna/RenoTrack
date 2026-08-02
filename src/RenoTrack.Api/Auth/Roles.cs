using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Api.Controllers;

/// <summary>
/// The two internal dashboard roles (Architecture.md §7.2), for use in
/// <c>[Authorize(Roles = ...)]</c> attributes and <c>User.IsInRole</c> checks.
/// </summary>
/// <remarks>
/// <para>
/// These deliberately <b>forward</b> to <see cref="IdentityRoleSeeder"/>'s constants rather than
/// repeating the literals. The names must match exactly what is seeded, and a mismatch fails in the
/// dangerous direction: <c>User.IsInRole("Inspecter")</c> is simply <c>false</c>, which a
/// scope-deriving helper would read as "not the scoped role" — the fail-open shape found and fixed
/// in Slice 6. Forwarding makes a typo impossible rather than merely unlikely.
/// </para>
/// <para>
/// The alias exists at all because <c>[Authorize(Roles = ...)]</c> needs a compile-time constant
/// expression, and a short <c>Roles.Admin</c> reads better at a dozen call sites than the seeder's
/// fully-qualified name.
/// </para>
/// </remarks>
public static class Roles
{
    public const string Admin = IdentityRoleSeeder.AdminRole;
    public const string Inspector = IdentityRoleSeeder.InspectorRole;
}
