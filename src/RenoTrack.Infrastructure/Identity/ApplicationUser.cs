using Microsoft.AspNetCore.Identity;

namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Infrastructure-only — must inherit IdentityUser&lt;int&gt;, and RenoTrack.Domain has zero
/// project references (D1), so this cannot live in Domain even as a matter of preference (D53).
/// Adds only what ERD.md's simplified USER sketch documents beyond IdentityUser's own base
/// columns (Email, PasswordHash, lockout fields, etc.): Name, IsActive (deactivation, resolving
/// SRS OQ-1 per PermissionMatrix.md's "Create/deactivate Inspector accounts"), CreatedAt.
/// </summary>
public sealed class ApplicationUser : IdentityUser<int>
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
