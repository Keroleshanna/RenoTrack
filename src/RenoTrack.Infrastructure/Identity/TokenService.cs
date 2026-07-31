using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Identity;

/// <inheritdoc cref="ITokenService" />
/// <remarks>
/// <para>
/// <b>Why this class exists rather than the logic living in <c>AuthController</c>:</b> token
/// issuance needs the signing key, the refresh-token table, and the roles query — three
/// Infrastructure concerns the controller has no business knowing about. Keeping them here leaves
/// the controller doing only what a controller should: decide which HTTP response a use case
/// produced. It is also what lets issuance and validation share one <see cref="JwtOptions"/>, so
/// the two can never drift apart.
/// </para>
/// <para>
/// <b>Why it writes through <see cref="RenoTrackDbContext"/> directly rather than a repository and
/// <c>IUnitOfWork</c>:</b> those abstractions exist to serve Application-layer handlers working
/// with Domain aggregates (CLAUDE.md §4). <see cref="RefreshToken"/> is neither — it is an
/// Infrastructure-only persistence model (D60), and adding an <c>IRefreshTokenRepository</c> to
/// <c>Application.Common.Interfaces</c> would put an authentication mechanism into the layer that
/// owns business use cases. This mirrors how <c>AuditService</c> and <c>NumberGeneratorService</c>
/// already persist their own Infrastructure-only tables.
/// </para>
/// <para>
/// Each public method commits its own writes, for the same reason <c>AuditService</c> does (D50):
/// no Application-layer <c>IUnitOfWork</c> is in play on the authentication path, so nothing else
/// would ever commit them.
/// </para>
/// </remarks>
public sealed class TokenService(RenoTrackDbContext dbContext, JwtOptions options) : ITokenService
{
    public async Task<TokenPair> IssueAsync(
        int userId,
        string email,
        string name,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var accessTokenExpiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var refreshTokenExpiresAt = now.AddDays(options.RefreshTokenDays);

        var accessToken = CreateAccessToken(userId, email, name, roles, now, accessTokenExpiresAt);

        var refreshToken = RefreshToken.GenerateToken();
        dbContext.RefreshTokens.Add(
            new RefreshToken(userId, RefreshToken.Hash(refreshToken), refreshTokenExpiresAt));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TokenPair(accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, userId);
    }

    public async Task<TokenPair?> RotateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var hash = RefreshToken.Hash(refreshToken);

        var stored = await dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        // Reuse detection: a revoked token being presented means either a stolen token is being
        // replayed or a client bug. Either way the safe response is to break the whole chain — if
        // it is a theft, the attacker and the legitimate user both hold live tokens right now, and
        // this is the only way to end the attacker's access. See ITokenService.RotateAsync.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            return null;
        }

        if (!stored.IsActive(now))
        {
            return null;
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);

        // Deactivating a user must take effect at the next refresh, not only at the next login —
        // otherwise a deactivated account keeps working for as long as it keeps rotating.
        if (user is null || !user.IsActive)
        {
            stored.Revoke(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        var roles = await GetRolesAsync(user.Id, cancellationToken);

        var newRefreshToken = RefreshToken.GenerateToken();
        var newHash = RefreshToken.Hash(newRefreshToken);
        var refreshTokenExpiresAt = now.AddDays(options.RefreshTokenDays);

        stored.Revoke(now, newHash);
        dbContext.RefreshTokens.Add(new RefreshToken(user.Id, newHash, refreshTokenExpiresAt));

        var accessTokenExpiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var accessToken = CreateAccessToken(user.Id, user.Email!, user.Name, roles, now, accessTokenExpiresAt);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new TokenPair(accessToken, accessTokenExpiresAt, newRefreshToken, refreshTokenExpiresAt, user.Id);
    }

    private async Task RevokeAllForUserAsync(int userId, DateTime now, CancellationToken cancellationToken)
    {
        var outstanding = await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in outstanding)
        {
            token.Revoke(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetRolesAsync(int userId, CancellationToken cancellationToken) =>
        await (from userRole in dbContext.UserRoles
               join role in dbContext.Roles on userRole.RoleId equals role.Id
               where userRole.UserId == userId
               select role.Name!)
            .ToListAsync(cancellationToken);

    private string CreateAccessToken(
        int userId,
        string email,
        string name,
        IEnumerable<string> roles,
        DateTime issuedAt,
        DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Name, name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // Emit whatever roles Identity actually holds rather than assuming exactly one: this
        // project's PermissionMatrix models two mutually-exclusive roles, but silently dropping a
        // second one would hide a mis-provisioned account instead of surfacing it. Enforcing
        // single-role membership is a user-management concern, and user management is not in
        // Phase 4's scope.
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Deliberately no AssignedInspectorId-style claim: ownership is decided from the loaded
        // aggregate by IOwnershipValidator (CLAUDE.md §16), never from a claim that could go stale
        // partway through a token's lifetime.

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
