namespace RenoTrack.Api.Controllers;

/// <summary>
/// The two internal dashboard roles (Architecture.md §7.2), as constants rather than repeated
/// string literals.
/// </summary>
/// <remarks>
/// These values must match exactly what <c>IdentityRoleSeeder</c> seeds — a typo in a role name
/// fails <em>open</em> for an ownership check (<c>User.IsInRole("Inspecter")</c> is false, so the
/// caller is treated as unscoped) and <em>closed</em> for an <c>[Authorize(Roles = ...)]</c>
/// attribute. The first of those is the dangerous direction, which is why the names live in one
/// place rather than being retyped per endpoint.
/// </remarks>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Inspector = "Inspector";
}
