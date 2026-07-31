using System.Security.Cryptography;

namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// A persisted, revocable refresh token (Architecture.md §7.1's "short-lived access token +
/// refresh token pattern"). Infrastructure-only, deliberately not a Domain entity — same reasoning
/// as <see cref="AuditLog"/> (D49) and NumberSequence (D51): it protects no business invariant, no
/// BusinessRules.md rule references it, and authentication is a mechanism rather than a business
/// concept. It never appears in RenoTrack.Domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a SHA-256 hash of the token is stored, never the plaintext.</b> The client receives the
/// plaintext exactly once, in the login/refresh response; every later lookup hashes the incoming
/// value and matches on the hash. A database read therefore yields no usable credential — the same
/// reasoning that applies to passwords. SHA-256 (not a password hash like PBKDF2) is correct here
/// because the input is already 32 bytes of cryptographic randomness, so it has no entropy to
/// stretch and is not brute-forceable.
/// </para>
/// <para>
/// <b>Lifecycle / retention (decided deliberately, not left to accident).</b> A row carries useful
/// information only until <see cref="ExpiresAt"/>: revoked-but-unexpired rows must be kept, because
/// they are exactly what makes reuse detection possible, but once expired a token is rejected on
/// expiry grounds regardless of its revocation state, so the row is dead weight. Anything past
/// <see cref="ExpiresAt"/> can be deleted at any time with zero behavioural change. No cleanup
/// mechanism is built today, on purpose: with 15-minute access tokens an active user produces
/// roughly 32 rows per working day, and with a 7-day retention window steady state is on the order
/// of (users x 32 x 7) rows — a few hundred for this company's real staff count. Revisit only if the
/// table reaches a size that actually matters (tens of thousands of rows, or an order-of-magnitude
/// increase in users); the fix then is a background cleanup job deleting rows past
/// <see cref="ExpiresAt"/>. Note CLAUDE.md §2's "never truly delete a historical record" rule does
/// not apply — that governs business records, not authentication mechanisms.
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// The hash of the token that superseded this one during rotation. Kept so a rotation chain can
    /// be followed; combined with <see cref="RevokedAt"/> it is what distinguishes "rotated
    /// normally" from "revoked because a stolen token was replayed."
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    public RefreshToken(int userId, string tokenHash, DateTime expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && utcNow < ExpiresAt;

    public void Revoke(DateTime utcNow, string? replacedByTokenHash = null)
    {
        // Idempotent: re-revoking an already-revoked token must not overwrite the original
        // timestamp, which is evidence of when the chain was first broken.
        RevokedAt ??= utcNow;
        ReplacedByTokenHash ??= replacedByTokenHash;
    }

    /// <summary>
    /// 32 bytes of cryptographic randomness, base64url-encoded — the same standard
    /// Architecture.md §7.2 specifies for customer token links, so this project has one way of
    /// producing unguessable tokens rather than two.
    /// </summary>
    public static string GenerateToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
