namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// Issues and rotates the token pair backing dashboard authentication.
/// </summary>
/// <remarks>
/// Declared in Infrastructure, not in <c>RenoTrack.Application.Common.Interfaces</c> like every
/// repository/service interface, because the Application layer neither consumes nor could consume
/// it: its inputs are <c>ApplicationUser</c> and Identity role data, both Infrastructure types
/// forced there by D53/D1. Putting it in Application would mean that layer declaring an abstraction
/// it never uses. Its only consumer is <c>AuthController</c>, and RenoTrack.Api already references
/// RenoTrack.Infrastructure directly. See D60 for why authentication sits outside the CQRS pipeline
/// entirely.
/// </remarks>
public interface ITokenService
{
    /// <summary>
    /// Issues a fresh access/refresh pair and persists the refresh token's hash. The returned
    /// refresh token is plaintext and is the only time it is ever available — only its hash is
    /// stored.
    /// </summary>
    Task<TokenPair> IssueAsync(int userId, string email, string name, IEnumerable<string> roles, CancellationToken cancellationToken);

    /// <summary>
    /// Validates and rotates a refresh token: the presented token is revoked and replaced by a new
    /// pair. Returns <c>null</c> when the token is unknown, expired, or already revoked — the
    /// caller maps every one of those to an indistinguishable 401.
    /// </summary>
    /// <remarks>
    /// Presenting an <em>already-revoked</em> token additionally revokes every outstanding token
    /// for that user. A revoked token reaching us means either a replay of a stolen token or a
    /// client bug; in the first case the attacker and the legitimate user both hold live tokens,
    /// and breaking the whole chain is the only way to end the attacker's access. Forcing one
    /// re-login is the correct trade against leaving a compromised session alive.
    /// </remarks>
    Task<TokenPair?> RotateAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <param name="AccessToken">Signed JWT.</param>
/// <param name="ExpiresAt">Access token expiry (UTC).</param>
/// <param name="RefreshToken">Plaintext refresh token — never persisted, only its hash is.</param>
/// <param name="RefreshTokenExpiresAt">Refresh token expiry (UTC).</param>
/// <param name="UserId">
/// Who the pair was issued to. Carried explicitly so a caller never has to parse the access token
/// back apart to find out — after <see cref="ITokenService.RotateAsync"/> the caller knows only the
/// refresh token it passed in, and reading the subject claim out of a JWT this same process just
/// signed would be a needless round trip through serialization.
/// </param>
public sealed record TokenPair(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    int UserId);
