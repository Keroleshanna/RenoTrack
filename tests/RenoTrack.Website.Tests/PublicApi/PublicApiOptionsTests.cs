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

    [Fact]
    public void A_relative_base_url_fails_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => With("api.example.de").Validate());

        Assert.Contains("absolute URL", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A leading-slash path is refused, and this test deliberately asserts only that — not which of
    /// the two guards reports it.
    /// </summary>
    /// <remarks>
    /// <b>Which branch fires is operating-system dependent.</b> On Unix,
    /// <c>Uri.TryCreate("/api/v1", UriKind.Absolute, …)</c> <i>succeeds</i>, yielding the
    /// <c>file</c> scheme, so the HTTPS guard rejects it; on Windows it is not an absolute URI at
    /// all and the absolute-URL guard rejects it instead. An earlier version of this test pinned the
    /// absolute-URL branch and failed on CI's Linux runner.
    ///
    /// Pinning the Linux branch instead would only move the problem: this suite runs on Linux in CI
    /// but a contributor runs it on Windows, and a test that passes in one place and fails in the
    /// other is exactly the failure mode <c>CLAUDE.md</c> §22 records for
    /// <c>Path.GetInvalidFileNameChars()</c>. What matters to the caller — the value is refused, and
    /// the message names the key at fault — is true on every OS, so that is what is asserted.
    /// </remarks>
    [Fact]
    public void A_leading_slash_path_is_refused_on_every_operating_system()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => With("/api/v1").Validate());

        Assert.Contains(
            $"{PublicApiOptions.SectionName}:{nameof(PublicApiOptions.BaseUrl)}",
            exception.Message,
            StringComparison.Ordinal);
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
