using Microsoft.Extensions.Configuration;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.Persistence.Entities;

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

    /// <summary>
    /// S3-5: an Admin notification is delivered to every configured address, and
    /// <c>NotificationDelivery.Recipient</c> records that complete set. The check measures the
    /// <b>exact persisted representation</b> — same join, same separator — so a configuration cannot
    /// pass startup and then fail at the database, stranding a successfully-sent notification as
    /// Pending.
    /// </summary>
    [Fact]
    public void Validate_Accepts_AnAdminRecipientListExactlyAtTheLimit()
    {
        string? address = new string('a', NotificationDelivery.MaxRecipientLength - "@example.invalid".Length) + "@example.invalid";
        Assert.Equal(NotificationDelivery.MaxRecipientLength, address!.Length);

        Read(Complete(("Email:AdminRecipients:0", address))).Validate();
    }

    [Fact]
    public void Validate_ThrowsNamingTheKey_WhenTheJoinedAdminRecipientListExceedsTheLimit()
    {
        string? address = new string('a', NotificationDelivery.MaxRecipientLength - "@example.invalid".Length + 1) + "@example.invalid";
        Assert.Equal(NotificationDelivery.MaxRecipientLength + 1, address!.Length);

        var options = Read(Complete(("Email:AdminRecipients:0", address)));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:AdminRecipients", exception.Message);
        Assert.Contains(NotificationDelivery.MaxRecipientLength.ToString(), exception.Message);
    }

    /// <summary>
    /// The limit applies to the joined set, not to any single address — several individually-valid
    /// addresses can exceed it together, which is the case a per-address check would miss.
    /// </summary>
    [Fact]
    public void Validate_Throws_WhenManyIndividuallyValidAddressesExceedTheLimitTogether()
    {
        var overrides = new List<(string, string?)>();

        // 30 addresses of 40 characters join to 30*40 + 29*2 = 1258 > 1000.
        for (var index = 0; index < 30; index++)
        {
            string? address = new string((char)('a' + (index % 26)), 40 - "@example.invalid".Length) + "@example.invalid";
            Assert.Equal(40, address!.Length);
            overrides.Add(($"Email:AdminRecipients:{index}", address));
        }

        var options = Read(Complete([.. overrides]));

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:AdminRecipients", exception.Message);
    }

    /// <summary>
    /// The check must measure the <b>exact persisted representation</b>, separator included — not an
    /// approximation of it.
    ///
    /// <para><b>Found by an adversarial experiment.</b> Measuring with <c>";"</c> instead of
    /// <see cref="NotificationDelivery.RecipientSeparator"/> broke nothing, because every other
    /// boundary test here uses a single address, where the separator never appears. The case below is
    /// deliberately separator-sensitive: 20 addresses of 49 characters join to <b>1018</b> with
    /// <c>", "</c> (over the limit, must be rejected) but only <b>999</b> with a one-character
    /// separator (under it, would wrongly pass). A mismatch would let a configuration clear startup
    /// and then fail at the database — exactly the failure this validation exists to prevent.</para>
    /// </summary>
    [Fact]
    public void Validate_MeasuresTheJoinedListUsingThePersistedSeparator()
    {
        var overrides = new List<(string, string?)>();

        for (var index = 0; index < 20; index++)
        {
            string? address = new string((char)('a' + (index % 26)), 49 - "@example.invalid".Length) + "@example.invalid";
            Assert.Equal(49, address!.Length);
            overrides.Add(($"Email:AdminRecipients:{index}", address));
        }

        var options = Read(Complete([.. overrides]));

        Assert.Equal(1018, string.Join(NotificationDelivery.RecipientSeparator, options.AdminRecipients).Length);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Email:AdminRecipients", exception.Message);
    }

    /// <summary>The list is never shortened — a truncated recipient set misreports who was mailed.</summary>
    [Fact]
    public void Validate_DoesNotTruncateTheAdminRecipientList()
    {
        var addresses = new[] { "office@example.invalid", "owner@example.invalid" };
        var options = Read(Complete(
            ("Email:AdminRecipients:0", addresses[0]),
            ("Email:AdminRecipients:1", addresses[1])));

        options.Validate();

        Assert.Equal(addresses, options.AdminRecipients);
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
