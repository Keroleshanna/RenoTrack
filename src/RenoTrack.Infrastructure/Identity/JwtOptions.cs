namespace RenoTrack.Infrastructure.Identity;

/// <summary>
/// JWT issuance/validation settings, bound from the <c>Jwt</c> configuration section. Lifetimes are
/// configuration rather than constants because they are operational knobs an operator may need to
/// change without a rebuild.
/// </summary>
/// <remarks>
/// <b>The signing key must never be committed.</b> It belongs in
/// <c>appsettings.Development.json</c> locally (which is developer-local, like the connection
/// string already is) and in environment variables or a secrets manager everywhere else
/// (Architecture.md §13). <see cref="Validate"/> fails startup loudly if it is missing or too
/// short, on the same fail-fast principle as AddInfrastructure's connection-string check — a
/// missing key must never silently degrade into an API that issues unverifiable tokens.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 keys shorter than the 256-bit hash output weaken the signature, and
    /// Microsoft's handler refuses them outright — 32 bytes is the real floor, not a stylistic one.
    /// </summary>
    public const int MinimumSigningKeyLength = 32;

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;

    /// <summary>
    /// Throws with a message naming the exact configuration key at fault, so a misconfigured
    /// deployment fails at startup with something actionable rather than at first login with a
    /// generic 500.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(Issuer)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(Audience)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException($"Configuration '{SectionName}:{nameof(SigningKey)}' is required.");
        }

        if (SigningKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(SigningKey)}' must be at least {MinimumSigningKeyLength} characters.");
        }

        if (AccessTokenMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(AccessTokenMinutes)}' must be greater than zero.");
        }

        if (RefreshTokenDays <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(RefreshTokenDays)}' must be greater than zero.");
        }
    }
}
