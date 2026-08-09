using Microsoft.Extensions.Configuration;
using RenoTrack.Infrastructure.Email;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// No database, no network. Every assertion here is about a configuration mistake failing at
/// startup with a message naming the exact key at fault, which is the whole reason these settings
/// are validated eagerly rather than discovered on the first notification.
/// </summary>
public sealed class EmailOptionsTests
{
    private static EmailOptions Read(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => setting.Value))
            .Build();

        return EmailOptions.FromConfiguration(configuration);
    }

    private static (string, string?)[] Complete(params (string, string?)[] overrides)
    {
        var settings = new List<(string, string?)>
        {
            ("Email:Enabled", "true"),
            ("Email:Host", "smtp.example.invalid"),
            ("Email:Port", "587"),
            ("Email:SecurityMode", "StartTls"),
            ("Email:FromAddress", "no-reply@example.invalid"),
            ("Email:FromDisplayName", "Example"),
            ("Email:AdminRecipients:0", "office@example.invalid"),
        };

        foreach (var (key, value) in overrides)
        {
            settings.RemoveAll(setting => setting.Item1 == key);

            if (value is not null)
            {
                settings.Add((key, value));
            }
        }

        return [.. settings];
    }

    [Fact]
    public void Enabled_IsFalse_WhenTheKeyIsAbsent()
    {
        Assert.False(Read().Enabled);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1 ")]
    [InlineData("True!")]
    [InlineData("enabled")]
    public void Enabled_ThrowsNamingTheKey_WhenTheValueIsNotABoolean(string configured)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(("Email:Enabled", configured)));

        Assert.Contains("Email:Enabled", exception.Message);
        Assert.Contains(configured, exception.Message);

        // Asserts *our* message, not the configuration binder's. The binder also throws on this key
        // ("Failed to convert configuration value…"), so without this the test would pass even with
        // ReadEnabled removed entirely — which an adversarial experiment demonstrated it did.
        Assert.Contains("Allowed values: true, false", exception.Message);
    }

    [Fact]
    public void A_complete_configuration_validates()
    {
        Read(Complete()).Validate();
    }

    [Theory]
    [InlineData("Email:Host", "Host")]
    [InlineData("Email:FromAddress", "FromAddress")]
    [InlineData("Email:FromDisplayName", "FromDisplayName")]
    public void Validate_ThrowsNamingTheKey_WhenARequiredValueIsMissing(string key, string expectedInMessage)
    {
        var options = Read(Complete((key, null)));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains(expectedInMessage, exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void Validate_ThrowsNamingThePort_WhenItIsOutOfRange(string port)
    {
        var options = Read(Complete(("Email:Port", port)));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:Port", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsNamingTheKey_WhenAnAddressIsNotParseable()
    {
        var options = Read(Complete(("Email:FromAddress", "not-an-address")));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:FromAddress", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsNamingTheIndexedKey_WhenAnAdminRecipientIsNotParseable()
    {
        var options = Read(Complete(("Email:AdminRecipients:1", "not-an-address")));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:AdminRecipients:1", exception.Message);
    }

    /// <summary>
    /// D71: an empty list must fail rather than raise FR-9.2's notifications and deliver them to
    /// nobody. This is the one "required" check whose absence would be silent at runtime.
    /// </summary>
    [Fact]
    public void Validate_Throws_WhenAdminRecipientsIsEmpty()
    {
        var options = Read(Complete(("Email:AdminRecipients:0", null)));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:AdminRecipients", exception.Message);
    }

    /// <summary>D6: credentials are all-or-nothing. Both absent is a legitimate unauthenticated relay.</summary>
    [Fact]
    public void Validate_Accepts_WhenNeitherCredentialIsConfigured()
    {
        Read(Complete()).Validate();
    }

    [Fact]
    public void Validate_Accepts_WhenBothCredentialsAreConfigured()
    {
        Read(Complete(("Email:Username", "smtp-user"), ("Email:Password", "smtp-secret"))).Validate();
    }

    [Fact]
    public void Validate_ThrowsNamingBothKeys_WhenOnlyTheUsernameIsConfigured()
    {
        var options = Read(Complete(("Email:Username", "smtp-user")));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:Username", exception.Message);
        Assert.Contains("Email:Password", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsNamingBothKeys_WhenOnlyThePasswordIsConfigured()
    {
        var options = Read(Complete(("Email:Password", "smtp-secret")));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:Username", exception.Message);
        Assert.Contains("Email:Password", exception.Message);
    }

    /// <summary>F8: absent means no Reply-To header at all, never an invented one.</summary>
    [Fact]
    public void ReplyToAddress_IsOptional()
    {
        Assert.Null(Read(Complete()).ReplyToAddress);
    }

    [Fact]
    public void Validate_ThrowsNamingTheKey_WhenReplyToIsConfiguredButUnparseable()
    {
        var options = Read(Complete(("Email:ReplyToAddress", "not-an-address")));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:ReplyToAddress", exception.Message);
    }
}
