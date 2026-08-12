using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.TokenLinks;

/// <summary>
/// <c>PublicBaseUrl</c> (D4.1). Validated separately from <see cref="TokenLinkOptions.Validate"/>
/// because nothing composes a customer link while email delivery is off — the key lives in the
/// TokenLink section, but its requiredness is governed by <c>Email:Enabled</c>.
/// </summary>
public sealed class TokenLinkOptionsTests
{
    private static TokenLinkOptions Options(string? publicBaseUrl) =>
        new() { LifetimeDays = 30, PublicBaseUrl = publicBaseUrl };

    [Fact]
    public void An_https_origin_is_accepted()
    {
        Options("https://www.example.invalid").ValidatePublicBaseUrl();
    }

    [Fact]
    public void An_absent_base_url_fails_and_names_both_keys()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Options(null).ValidatePublicBaseUrl());

        Assert.Contains("TokenLink:PublicBaseUrl", exception.Message);
        Assert.Contains("Email:Enabled", exception.Message);
    }

    [Fact]
    public void A_relative_url_fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Options("/angebot").ValidatePublicBaseUrl());

        Assert.Contains("TokenLink:PublicBaseUrl", exception.Message);
    }

    /// <summary>
    /// Architecture.md §12 requires HTTPS everywhere, and the link carries the credential that grants
    /// access to an Angebot or Invoice — sending it over plaintext would put that credential on the
    /// wire.
    /// </summary>
    [Fact]
    public void An_http_url_fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Options("http://www.example.invalid").ValidatePublicBaseUrl());

        Assert.Contains("HTTPS", exception.Message);
    }

    [Fact]
    public void The_normalized_origin_has_no_trailing_slash()
    {
        Assert.Equal("https://www.example.invalid", Options("https://www.example.invalid/").NormalizedPublicBaseUrl);
        Assert.Equal("https://www.example.invalid", Options("https://www.example.invalid").NormalizedPublicBaseUrl);
    }

    /// <summary>
    /// The lifetime check is unconditional and must stay that way: every host issues token links,
    /// whether or not it mails them.
    /// </summary>
    [Fact]
    public void Validate_still_only_checks_the_lifetime()
    {
        Options(publicBaseUrl: null).Validate();
    }
}
