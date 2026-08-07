using System.Security.Cryptography;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.TokenLinks;

/// <summary>
/// Architecture.md §7.2 and §12: a cryptographically random 32-byte token, URL-safe base64,
/// explicitly "not derived from predictable data (e.g. not entityId + timestamp)".
///
/// <see cref="RandomNumberGenerator"/>, never <see cref="Random"/> — the same choice RefreshToken
/// already makes for the same reason. <c>Random</c> is seeded predictably and is not designed to
/// resist an attacker who has observed previous outputs, which is exactly the threat here: this
/// token is the *only* thing standing between an anonymous caller and a customer's priced quote.
///
/// 32 bytes is 256 bits of entropy, so guessing is not a realistic attack even before the rate
/// limiting Architecture.md §12 requires on the public routes (Slice 4).
/// </summary>
public sealed class TokenLinkService(TokenLinkOptions options) : ITokenLinkService
{
    private const int TokenBytes = 32;

    public GeneratedToken Generate() => new(
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes)),
        DateTime.UtcNow.AddDays(options.LifetimeDays));

    /// <summary>
    /// Base64url per RFC 4648 §5 — the token travels in a URL path segment, where '+' and '/'
    /// carry their own meaning and '=' padding is noise. Same encoding RefreshToken uses.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
