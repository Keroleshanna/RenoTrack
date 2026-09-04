using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// Wiring, not content: an absent or plaintext API origin must fail startup naming the exact key,
/// the same shape every other options type in this solution uses.
/// </summary>
public sealed class PublicApiOptionsTests
{
    private static PublicApiOptions With(string? baseUrl, int timeoutSeconds = PublicApiOptions.DefaultTimeoutSeconds) =>
        new() { BaseUrl = baseUrl, TimeoutSeconds = timeoutSeconds };

    [Fact]
    public void A_valid_https_origin_passes()
    {
        var exception = Record.Exception(() => With("https://api.example.de").Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_base_url_fails_startup_naming_the_key(string? baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => With(baseUrl).Validate());

        Assert.Contains($"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("api.example.de")]
    [InlineData("/api/v1")]
    public void A_relative_base_url_fails_startup(string baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => With(baseUrl).Validate());

        Assert.Contains("absolute URL", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The customer's token travels in this request's path, so a plaintext hop would put a live
    /// credential on the wire — Architecture.md §12, the same rule
    /// <c>TokenLinkOptions.ValidatePublicBaseUrl</c> enforces on the emailed URL. Refused in every
    /// environment, including for localhost: "it is only development" is how a plaintext default
    /// reaches production.
    /// </summary>
    [Theory]
    [InlineData("http://api.example.de")]
    [InlineData("http://localhost:5294")]
    public void A_plaintext_base_url_is_refused_everywhere(string baseUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => With(baseUrl).Validate());

        Assert.Contains("HTTPS is required", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_timeout_fails_startup(int timeoutSeconds)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => With("https://api.example.de", timeoutSeconds).Validate());

        Assert.Contains(nameof(PublicApiOptions.TimeoutSeconds), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// HttpClient's own default is 100 seconds, which is not a policy anyone chose for a page a
    /// customer is waiting on. Pinned so raising it becomes a deliberate edit.
    /// </summary>
    [Fact]
    public void The_default_timeout_is_ten_seconds_not_the_http_client_default()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), With("https://api.example.de").Timeout);
    }

    [Theory]
    [InlineData("https://api.example.de", "https://api.example.de")]
    [InlineData("https://api.example.de/", "https://api.example.de")]
    [InlineData("https://api.example.de///", "https://api.example.de")]
    public void The_normalized_origin_carries_no_trailing_slash(string configured, string expected)
    {
        Assert.Equal(expected, With(configured).NormalizedBaseUrl);
    }
}
