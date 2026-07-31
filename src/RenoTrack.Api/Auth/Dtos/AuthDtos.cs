namespace RenoTrack.Api.Auth.Dtos;

/// <param name="Email">SRS FR-10.1 — staff log in with email + password.</param>
public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// Login/refresh response. Carries the user's own details alongside the tokens so the Dashboard can
/// render "signed in as ..." without decoding the JWT client-side — the token already contains the
/// same values, so this exposes nothing new; it just avoids pushing token parsing into Angular for
/// data the server can simply state.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    int UserId,
    string Name,
    string Email,
    string Role);
