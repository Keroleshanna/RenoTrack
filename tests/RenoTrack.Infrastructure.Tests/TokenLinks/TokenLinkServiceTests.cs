using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.TokenLinks;

/// <summary>
/// No database involved — the same category as LocalDiskFileStorageTests and
/// LoggingNoOpEmailSenderTests. The properties under test are Architecture.md §7.2's and §12's:
/// unguessable, URL-safe, and not derived from predictable data.
/// </summary>
public sealed class TokenLinkServiceTests
{
    private static TokenLinkService CreateService(int lifetimeDays = 30) =>
        new(new TokenLinkOptions { LifetimeDays = lifetimeDays });

    /// <summary>
    /// 32 bytes base64url-encoded, padding stripped, is 43 characters. Pinned so a future change to
    /// the byte count is a deliberate edit here rather than a silent entropy reduction.
    /// </summary>
    [Fact]
    public void Generate_ProducesA43CharacterToken() =>
        Assert.Equal(43, CreateService().Generate().Token.Length);

    /// <summary>
    /// The token travels in a URL path segment, so '+', '/' and '=' must never appear —
    /// base64url per RFC 4648 §5, the same encoding RefreshToken uses.
    /// </summary>
    [Fact]
    public void Generate_ProducesAUrlSafeToken()
    {
        var token = CreateService().Generate().Token;

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"Unexpected character '{c}'."));
    }

    /// <summary>
    /// The one property the whole mechanism rests on. A thousand draws is far too few to prove
    /// randomness, but it is more than enough to catch the failure that would actually happen —
    /// a generator accidentally seeded once, or derived from a timestamp, repeating itself.
    /// </summary>
    [Fact]
    public void Generate_ProducesADistinctTokenEveryTime()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 1_000).Select(_ => service.Generate().Token).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(1_000, tokens.Count);
    }

    [Fact]
    public void Generate_SetsExpiryFromTheConfiguredLifetime()
    {
        var before = DateTime.UtcNow;

        var expiresAt = CreateService(lifetimeDays: 7).Generate().ExpiresAt;

        Assert.InRange(expiresAt, before.AddDays(7), DateTime.UtcNow.AddDays(7));
    }

    [Fact]
    public void Generate_ProducesAnExpiryInTheFuture() =>
        Assert.True(CreateService(lifetimeDays: 1).Generate().ExpiresAt > DateTime.UtcNow);

    // ---- Options validation (same fail-fast shape as FileStorageOptions) ----------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithANonPositiveLifetime_ThrowsNamingTheKey(int lifetimeDays)
    {
        var options = new TokenLinkOptions { LifetimeDays = lifetimeDays };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains($"{TokenLinkOptions.SectionName}:{nameof(TokenLinkOptions.LifetimeDays)}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WithAPositiveLifetime_DoesNotThrow() =>
        new TokenLinkOptions { LifetimeDays = 30 }.Validate();
}
