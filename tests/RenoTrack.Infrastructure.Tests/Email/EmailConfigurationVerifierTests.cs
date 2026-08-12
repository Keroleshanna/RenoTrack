using Microsoft.Extensions.Hosting;
using RenoTrack.Infrastructure.Email;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// The Production refuse-by-default guard (S1-3). Deliberately a startup step rather than a check
/// inside <c>AddInfrastructure</c>: a composition-time guard would have fired inside
/// <c>DevelopmentBootstrapTests</c> and <c>DatabaseInitializerTests</c>, which compose the real
/// container under Production with no email configuration, and would have pre-empted D64's tested
/// guard ordering.
/// </summary>
public sealed class EmailConfigurationVerifierTests
{
    private static void Verify(bool enabled, string environmentName) =>
        new EmailConfigurationVerifier(
            new EmailOptions { Enabled = enabled },
            new TestHostEnvironment(environmentName)).Verify();

    [Fact]
    public void Production_without_email_enabled_fails_and_names_the_key()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Verify(enabled: false, Environments.Production));

        Assert.Contains("Email:Enabled", exception.Message);
        Assert.Contains("Production", exception.Message);
    }

    [Fact]
    public void Production_with_email_enabled_passes()
    {
        Verify(enabled: true, Environments.Production);
    }

    /// <summary>
    /// Development and Test may leave the key absent and resolve <c>LoggingNoOpEmailSender</c> —
    /// which is exactly what both test projects and every developer machine do.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData("")]
    public void Every_other_environment_passes_without_email_configuration(string environmentName)
    {
        Verify(enabled: false, environmentName);
    }
}
