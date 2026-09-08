using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Api.RateLimiting;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Api.Tests.Email;

/// <summary>
/// Boots the real API with email delivery genuinely <b>enabled</b>, pointed at an in-process SMTP
/// server, so the customer workflow can be driven the way a customer actually experiences it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not <see cref="RenoTrackApiFactory"/>.</b> That one is a collection fixture built
/// once for every API test, with email left disabled so <c>LoggingNoOpEmailSender</c> is resolved.
/// This host must be constructed <em>after</em> the SMTP server, because the listener's port is
/// chosen by the OS and is only knowable once it is running — so it takes the port as a constructor
/// argument. It also uses its own database, so the two suites cannot interfere even if their
/// processes overlap.
/// </para>
/// <para>
/// <b>Everything below the test is the real thing.</b> Real Identity, real LocalDB, the real
/// notification pipeline, and the real <c>SmtpEmailSender</c> over a real socket — no fake
/// <c>IEmailSender</c> anywhere. What is substituted is the mail <em>server</em>, which is what a
/// developer substitutes with Mailpit; the client, the transport and the message are production
/// code.
/// </para>
/// </remarks>
public sealed class CustomerWorkflowE2EFactory(int smtpPort)
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=RenoTrackCustomerWorkflowE2ETests;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// The origin the customer's emailed link points at.
    /// </summary>
    /// <remarks>
    /// The test asserts the captured link starts with exactly this, which is what proves
    /// <c>TokenLink:PublicBaseUrl</c> reaches the customer rather than the API's own origin — the
    /// setting a real deployment gets wrong and only discovers when a customer clicks.
    /// </remarks>
    public const string PublicBaseUrl = "https://kunde.example.test";

    public const string AdminEmail = "e2e-admin@renotrack.test";
    public const string AdminPassword = "Admin#Pass123";
    public const string InspectorEmail = "e2e-inspector@renotrack.test";
    public const string InspectorPassword = "Inspector#Pass123";

    public const string AdminNotificationRecipient = "buero@example.test";

    /// <summary>Every log message the host wrote, so the test can prove no token reached one.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Where <c>LocalDiskFileStorage</c> would write. Nothing in this workflow uploads anything, but
    /// the option is validated at composition, so a value is required for the host to start at all.
    /// Unique per instance and removed on teardown, like <see cref="RenoTrackApiFactory"/>'s.
    /// </summary>
    private string StorageRoot { get; } =
        Path.Combine(Path.GetTempPath(), "RenoTrackCustomerWorkflowE2E", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RenoTrackDb", ConnectionString);

        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.Issuer)}", "RenoTrack.Api.Tests");
        builder.UseSetting($"{JwtOptions.SectionName}:{nameof(JwtOptions.Audience)}", "RenoTrack.Api.Tests");
        builder.UseSetting(
            $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}",
            "api-tests-signing-key-long-enough-for-hmac-sha256");

        // Required by AddInfrastructure's eager validation, which runs for every host regardless of
        // whether anything uploads a file. Omitting it failed all four tests at startup on the first
        // CI run — the guard doing exactly its job, on a harness that had not supplied the key.
        builder.UseSetting($"{FileStorageOptions.SectionName}:{nameof(FileStorageOptions.RootPath)}", StorageRoot);

        builder.UseSetting(DatabaseInitializationOptions.ModeKey, nameof(DatabaseInitializationMode.Migrate));
        builder.UseSetting(DevelopmentBootstrapOptions.EnabledKey, "false");

        // The public decision endpoint is rate limited and TestServer supplies no client address, so
        // every request here shares one partition. Raised for the same reason RenoTrackApiFactory
        // raises it: the limiter stays genuinely in the pipeline, it simply must not be what fails
        // this test. Rejection behaviour is proven separately by PublicRateLimitEndpointTests.
        builder.UseSetting(
            $"{PublicRateLimitOptions.SectionName}:{nameof(PublicRateLimitOptions.PermitLimit)}", "10000");

        // What the customer's emailed link points at.
        builder.UseSetting($"{TokenLinkOptions.SectionName}:{nameof(TokenLinkOptions.LifetimeDays)}", "30");
        builder.UseSetting($"{TokenLinkOptions.SectionName}:{nameof(TokenLinkOptions.PublicBaseUrl)}", PublicBaseUrl);

        // The point of this factory. Enabled means AddInfrastructure resolves SmtpEmailSender and
        // validates these settings eagerly; SecurityMode None with no credentials is exactly what a
        // local catcher offers, and is why no code change is needed to talk to one.
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.Enabled)}", "true");
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.Host)}", "127.0.0.1");
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.Port)}", smtpPort.ToString());
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.SecurityMode)}", nameof(EmailSecurityMode.None));
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.FromAddress)}", "no-reply@example.test");
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.FromDisplayName)}", "RenoTrack E2E");
        builder.UseSetting($"{EmailOptions.SectionName}:{nameof(EmailOptions.AdminRecipients)}:0", AdminNotificationRecipient);

        // Everything the host logs, kept so the test can search it for the credential. Information
        // level deliberately: the categories that would leak a token log at Information, so
        // capturing only warnings would make the assertion unable to fail.
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(Logs);
        });
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedUserAsync(userManager, AdminEmail, AdminPassword, "E2E Admin", IdentityRoleSeeder.AdminRole);
        await SeedUserAsync(userManager, InspectorEmail, InspectorPassword, "E2E Inspector", IdentityRoleSeeder.InspectorRole);
    }

    public async Task<int> GetUserIdAsync(string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Test user '{email}' was not seeded.");

        return user.Id;
    }

    private static async Task SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string name,
        string role)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = name,
            IsActive = true,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed test user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
    }

    public new async Task DisposeAsync()
    {
        await using (var context = CreateDbContext())
        {
            await context.Database.EnsureDeletedAsync();
        }

        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }

        await base.DisposeAsync();
    }

    private static RenoTrackDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RenoTrackDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new RenoTrackDbContext(options);
    }
}

/// <summary>
/// Records every message the host logs, so a test can assert what did <em>not</em> reach a log.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> §22 requires asserting log content, not only response bodies, wherever a route
/// carries a secret — the redaction defect it records went unnoticed precisely because no test
/// inspected the log.
/// </remarks>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<string> Messages
    {
        get { lock (_sync) { return [.. _messages]; } }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Record(string message)
    {
        lock (_sync)
        {
            _messages.Add(message);
        }
    }

    private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The exception is included because a stack trace or message is just as capable of
            // carrying a credential as the formatted line is.
            owner.Record($"{category} {logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}
