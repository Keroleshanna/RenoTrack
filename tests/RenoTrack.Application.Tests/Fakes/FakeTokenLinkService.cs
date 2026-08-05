using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Deterministic stand-in for the real CSPRNG-backed generator. Produces a predictable, distinct
/// token per call so a test can assert which token reached the notification — the real service's
/// randomness is its own concern and is tested against the real implementation in
/// RenoTrack.Infrastructure.Tests, not simulated here.
/// </summary>
public sealed class FakeTokenLinkService : ITokenLinkService
{
    private int _callCount;

    public int CallCount => _callCount;

    /// <summary>Overridable so a test can pin an expiry without depending on wall-clock arithmetic.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

    public GeneratedToken Generate()
    {
        _callCount++;
        return new GeneratedToken($"fake-token-{_callCount}", DateTime.UtcNow.Add(Lifetime));
    }
}
