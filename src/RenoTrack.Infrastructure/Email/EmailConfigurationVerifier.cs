using Microsoft.Extensions.Hosting;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Refuses to start a Production host that would silently send no email (S1-3).
///
/// <para><b>Why a startup step rather than a check inside <c>AddInfrastructure</c>.</b> The guard
/// needs <see cref="IHostEnvironment"/>, which <c>AddInfrastructure(services, configuration)</c>
/// cannot see; giving it that parameter would have changed seven call sites and — the real problem —
/// broken the guard *ordering* D63 and D64 deliberately test. <c>DevelopmentBootstrapTests</c> and
/// <c>DatabaseInitializerTests</c> compose the real container under <c>Production</c> with no email
/// configuration, and a composition-time email guard would have thrown before those tests reached
/// their subject, telling an operator about email when the actionable problem was something else.
/// Resolving the environment from DI at startup, exactly as <c>DatabaseInitializer</c> does (D63),
/// keeps the fail-fast property without disturbing either.</para>
///
/// <para><b>Direction matters.</b> D64's refuse-by-default protects against *doing* something —
/// minting a privileged account — so it refuses hardest in Production. Email's risk runs the other
/// way: a Production host that quietly mails nobody is an FR-9.1/FR-9.2 outage that surfaces as a
/// customer complaint weeks later. This is therefore D64's mirror, not a copy of it.</para>
/// </summary>
public sealed class EmailConfigurationVerifier(EmailOptions options, IHostEnvironment environment)
{
    /// <summary>
    /// No-op everywhere except Production, where <c>Email:Enabled</c> must be explicitly true.
    /// Development and Test may leave it absent or false and resolve
    /// <see cref="LoggingNoOpEmailSender"/>.
    ///
    /// <para>Nothing here validates the individual settings: <c>AddInfrastructure</c> has already
    /// done that eagerly whenever <c>Enabled</c> is true, so by the time this runs, "enabled" and
    /// "fully configured" mean the same thing.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Production, and email delivery is not enabled.</exception>
    public void Verify()
    {
        if (!environment.IsProduction() || options.Enabled)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Configuration '{EmailOptions.EnabledKey}' must be true in the Production environment. " +
            "SRS FR-9.1 and FR-9.2 require real notifications, and this host would otherwise start " +
            "normally while silently delivering none of them. Set it to true and supply the " +
            $"'{EmailOptions.SectionName}' settings, or run this host in a non-production environment.");
    }
}
