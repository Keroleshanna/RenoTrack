using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure;
using RenoTrack.Infrastructure.Email;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// Which <see cref="IEmailSender"/> the real composition resolves, proven against the real
/// <c>AddInfrastructure</c> rather than by reading it.
///
/// <para><b>Selection depends on <c>Email:Enabled</c> alone</b> (S1-3) — no environment is consulted
/// at composition, which is what lets <c>AddInfrastructure</c> keep its signature and leaves D63/D64's
/// Production-composing tests untouched.</para>
/// </summary>
public sealed class EmailDependencyInjectionTests
{
    private static Dictionary<string, string?> BaseSettings() => new()
    {
        ["ConnectionStrings:RenoTrackDb"] = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "RenoTrackEmailDiTests",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
        }.ConnectionString,
        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "RenoTrackEmailDiTests"),
        ["TokenLink:LifetimeDays"] = "30",
    };

    private static Dictionary<string, string?> EnabledSettings()
    {
        var settings = BaseSettings();
        settings["Email:Enabled"] = "true";
        settings["Email:Host"] = "smtp.example.invalid";
        settings["Email:Port"] = "587";
        settings["Email:SecurityMode"] = "StartTls";
        settings["Email:FromAddress"] = "no-reply@example.invalid";
        settings["Email:FromDisplayName"] = "Beispiel Bau GmbH";
        settings["Email:AdminRecipients:0"] = "office@example.invalid";
        settings["TokenLink:PublicBaseUrl"] = "https://www.example.invalid";
        return settings;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// The regression guard for conditional validation: a container with **no** <c>Email:*</c> keys
    /// must still compose. That is what every non-production host looks like, and what both
    /// <c>DependencyInjectionTests</c> classes already rely on.
    /// </summary>
    [Fact]
    public void Without_email_configuration_the_no_op_sender_is_resolved()
    {
        using var provider = BuildProvider(BaseSettings());
        using var scope = provider.CreateScope();

        Assert.IsType<LoggingNoOpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void With_email_enabled_the_smtp_sender_is_resolved()
    {
        using var provider = BuildProvider(EnabledSettings());
        using var scope = provider.CreateScope();

        Assert.IsType<SmtpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Enabling_email_without_a_host_fails_at_registration_time()
    {
        var settings = EnabledSettings();
        settings.Remove("Email:Host");

        var exception = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.Contains("Email:Host", exception.Message);
    }

    /// <summary>
    /// The cross-section coupling D4.1 creates: the key lives under <c>TokenLink</c>, but delivery is
    /// what makes it required, because nothing else composes a customer link today.
    /// </summary>
    [Fact]
    public void Enabling_email_without_a_public_base_url_fails_at_registration_time()
    {
        var settings = EnabledSettings();
        settings.Remove("TokenLink:PublicBaseUrl");

        var exception = Assert.Throws<InvalidOperationException>(() => BuildProvider(settings));

        Assert.Contains("TokenLink:PublicBaseUrl", exception.Message);
    }

    [Fact]
    public void Without_email_configuration_a_missing_public_base_url_is_not_required()
    {
        using var provider = BuildProvider(BaseSettings());

        Assert.NotNull(provider);
    }
}
