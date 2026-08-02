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
        var replacement = new RefreshToken(user.Id, newHash, refreshTokenExpiresAt);
        dbContext.RefreshTokens.Add(replacement);

        var accessTokenExpiresAt = now.AddMinutes(options.AccessTokenMinutes);
        var accessToken = CreateAccessToken(user.Id, user.Email!, user.Name, roles, now, accessTokenExpiresAt);

        try
        {
            // One SaveChangesAsync, so the revocation and its replacement share a single transaction:
            // there is never a moment where the old token is dead and no successor exists, or where a
            // successor exists alongside a still-live predecessor.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this same token between our read and our write. RevokedAt is a
            // concurrency token, so that UPDATE matched zero rows — and because EF wraps SaveChanges
            // in one transaction, the replacement INSERT rolled back with it. Exactly one caller wins
            // and exactly one live chain exists.
            //
            // Deliberately NOT treated as reuse: the loser is a legitimate concurrent request, not a
            // replay, and revoking the whole chain here would let any client with a double-submit bug
            // log itself out. Returning null yields the same 401 as every other refresh failure, so a
            // caller still cannot distinguish this from an unknown or revoked token.
            //
            // The tracked entities are detached because this DbContext is request-scoped and shared:
            // leaving a failed INSERT sitting in Added state would let any later SaveChangesAsync on
            // the same request commit it — the hazard Slice 9 found with AuditService.
            dbContext.Entry(replacement).State = EntityState.Detached;
            dbContext.Entry(stored).State = EntityState.Detached;

            return null;
        }

        return new TokenPair(accessToken, accessTokenExpiresAt, newRefreshToken, refreshTokenExpiresAt, user.Id);
    }

    /// <summary>
    /// Revokes every outstanding token for a user — the response to a replayed (already-revoked)
    /// token, where we cannot tell the legitimate holder from the thief.
    /// </summary>
    /// <remarks>
    /// A set-based <c>ExecuteUpdateAsync</c> rather than load-mutate-save, and that is load-bearing
    /// rather than an optimisation. Once <c>RevokedAt</c> became a concurrency token, the previous
    /// implementation could throw <c>DbUpdateConcurrencyException</c> whenever a concurrent request
    /// revoked one of the same rows first — which surfaced as an unmapped **500** under concurrent
    /// replay, and would additionally have rolled the whole batch back, leaving tokens live that
    /// nothing else was going to revoke. Found by the concurrency test added alongside this fix, not
    /// by inspection.
    ///
    /// A single conditional <c>UPDATE ... WHERE UserId = @id AND RevokedAt IS NULL</c> states the
    /// intent exactly — "no live token survives for this user" — is atomic at the database, and
    /// cannot conflict with the change tracker because it bypasses it. It is still EF Core LINQ, so
    /// D52's narrowly-scoped raw-SQL exception does not come into play.
    ///
    /// <c>ReplacedByTokenHash</c> is deliberately left untouched: these are terminal revocations, not
    /// rotations, so there is no successor to record.
    /// </remarks>
    private async Task RevokeAllForUserAsync(int userId, DateTime now, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);

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
