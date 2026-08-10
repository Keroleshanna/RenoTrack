using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Tests.Persistence;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// Transport behaviour, against a real socket and the real MailKit client (see
/// <see cref="InProcessSmtpServer"/>). Uses the shared LocalDB collection because
/// <see cref="InspectorEmailLookup"/> needs a real <see cref="RenoTrackDbContext"/>.
///
/// <para><b>Slice 1 catches nothing.</b> Several tests here assert that a failure *propagates* —
/// that is the deliverable of this slice, not an oversight. Catch / log / never-rethrow is Slice 2's,
/// and adding it now would leave Slice 2's adversarial test with nothing to remove.</para>
/// </summary>
[Collection("Infrastructure Database")]
public sealed class SmtpEmailSenderTests(RenoTrackDbContextFixture fixture)
{
    private static EmailOptions Options(int port, string? username = null, string? password = null) => new()
    {
        Enabled = true,
        Host = "127.0.0.1",
        Port = port,
        SecurityMode = EmailSecurityMode.None,
        Username = username,
        Password = password,
        FromAddress = "no-reply@example.invalid",
        FromDisplayName = "Beispiel Bau GmbH",
        AdminRecipients = ["office@example.invalid"],
    };

    private SmtpEmailSender CreateSender(EmailOptions options, CapturingLoggerProvider? logProvider = null)
    {
        var tokenLinkOptions = new TokenLinkOptions { LifetimeDays = 30, PublicBaseUrl = "https://www.example.invalid" };

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            if (logProvider is not null)
            {
                builder.AddProvider(logProvider);
            }
        });

        return new SmtpEmailSender(
            options,
            new EmailMessageFactory(options, tokenLinkOptions),
            new InspectorEmailLookup(fixture.CreateContext()),
            loggerFactory.CreateLogger<SmtpEmailSender>());
    }

    private static readonly AngebotReadyNotification AngebotReady =
        new(5, "ANG-2026-00005", "Familie Klein", "klein@example.invalid", "tok-abc123");

    [Fact]
    public async Task A_notification_is_delivered_over_a_real_socket()
    {
        await using var server = new InProcessSmtpServer();
        var sender = CreateSender(Options(server.Port));

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        var message = Assert.Single(server.Messages);
        Assert.Contains("Ihr Angebot ANG-2026-00005", message);
        Assert.Contains("klein@example.invalid", message);
    }

    [Fact]
    public async Task Authentication_is_attempted_only_when_both_credentials_are_configured()
    {
        await using var authenticated = new InProcessSmtpServer(advertiseAuthentication: true);
        await CreateSender(Options(authenticated.Port, "smtp-user", "smtp-secret"))
            .SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        Assert.Contains(authenticated.Commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));

        await using var anonymous = new InProcessSmtpServer(advertiseAuthentication: true);
        await CreateSender(Options(anonymous.Port))
            .SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        Assert.DoesNotContain(anonymous.Commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Slice 2's defining behaviour (D69). Updated from Slice 1, where this same test asserted the
    /// opposite — that was correct while no failure boundary existed, and is deliberately inverted
    /// now that one does, not a defect being fixed.
    /// </summary>
    [Fact]
    public async Task A_refused_connection_is_swallowed_and_logged()
    {
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(ClosedPort()), logProvider);

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        var warning = AssertSingleWarning(logProvider);
        Assert.Contains(nameof(IEmailSender.SendAngebotReadyNotificationAsync), warning.Message);
        Assert.Contains("ANG-2026-00005", warning.Message);
    }

    /// <summary>
    /// Cancellation still cancels the SMTP operation — nothing is delivered — but it does not escape
    /// the boundary: the business operation has already committed, so reporting a cancelled
    /// notification as a failed request would misdescribe what happened (approved Slice 2 decision).
    /// </summary>
    [Fact]
    public async Task Cancellation_is_observed_and_does_not_escape()
    {
        await using var server = new InProcessSmtpServer();
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(server.Port), logProvider);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, cancellation.Token);

        Assert.Empty(server.Messages);

        var warning = AssertSingleWarning(logProvider);
        Assert.IsAssignableFrom<OperationCanceledException>(warning.Exception);
    }

    /// <summary>
    /// CLAUDE.md §22: the token is the credential that grants access to the Angebot. It belongs in
    /// the message and nowhere else — not in a log line, not in a composed URL written to a log.
    /// Asserted against real log output rather than by reading the code.
    /// </summary>
    [Fact]
    public async Task The_token_and_link_never_reach_the_log()
    {
        await using var server = new InProcessSmtpServer();
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(server.Port), logProvider);

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        var entries = logProvider.EntriesFrom<SmtpEmailSender>();
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            Assert.DoesNotContain("tok-abc123", entry.Message);
            Assert.DoesNotContain("/angebot/", entry.Message);
            Assert.DoesNotContain("klein@example.invalid", entry.Message);
        }

        // The token *is* in the message — proving the assertions above are not passing because
        // nothing was rendered at all.
        Assert.Contains("tok-abc123", Assert.Single(server.Messages));
    }

    /// <summary>
    /// D2: a missing Inspector address is a delivery failure, never a silent skip. Updated for
    /// Slice 2: "failure" now means reported and swallowed rather than thrown, and the recipient
    /// resolution sits inside the guarded region so it is treated like any other delivery failure.
    /// </summary>
    [Fact]
    public async Task A_missing_inspector_address_is_swallowed_and_logged_rather_than_skipped_silently()
    {
        await using var server = new InProcessSmtpServer();
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(server.Port), logProvider);

        await sender.SendAngebotChangesRequestedNotificationAsync(
            new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Bitte korrigieren.", InspectorId: 999_999),
            CancellationToken.None);

        Assert.Empty(server.Messages);

        var warning = AssertSingleWarning(logProvider);
        Assert.IsType<InvalidOperationException>(warning.Exception);
        Assert.Contains("999999", warning.Exception!.Message);
    }

    /// <summary>
    /// Message construction is inside the boundary, not outside it. A stored address that MimeKit
    /// cannot parse is exactly the failure that would escape if the message were built at the call
    /// site and handed in already constructed.
    /// </summary>
    [Fact]
    public async Task A_malformed_recipient_address_is_swallowed_and_logged()
    {
        await using var server = new InProcessSmtpServer();
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(server.Port), logProvider);

        await sender.SendAngebotReadyNotificationAsync(
            new AngebotReadyNotification(5, "ANG-2026-00005", "Familie Klein", "not a valid address @@", "tok-abc123"),
            CancellationToken.None);

        Assert.Empty(server.Messages);
        AssertSingleWarning(logProvider);
    }

    /// <summary>Every one of the six notifications shares the same boundary — none is left unguarded.</summary>
    [Fact]
    public async Task Every_notification_type_swallows_a_transport_failure()
    {
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(ClosedPort()), logProvider);
        var inspectorId = await SeedInspectorAsync($"guarded-{Guid.NewGuid():N}@example.invalid", isActive: true);

        await sender.SendNewWebsiteLeadNotificationAsync(
            new NewWebsiteLeadNotification(7, "Familie Klein", "0176 1234567", "klein@example.invalid"), CancellationToken.None);
        await sender.SendAngebotSubmittedForReviewNotificationAsync(
            new AngebotSubmittedForReviewNotification(5, "ANG-2026-00005", 7), CancellationToken.None);
        await sender.SendAngebotChangesRequestedNotificationAsync(
            new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Bitte korrigieren.", inspectorId), CancellationToken.None);
        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);
        await sender.SendInvoiceReadyNotificationAsync(
            new InvoiceReadyNotification(9, "RE-2026-00009", "Familie Klein", "klein@example.invalid", 1234.56m, new DateTime(2026, 8, 31), "tok-xyz789"),
            CancellationToken.None);
        await sender.SendAngebotDecisionNotificationAsync(
            new AngebotDecisionNotification(5, "ANG-2026-00005", 7, "Familie Klein", Approved: true), CancellationToken.None);

        var warnings = logProvider.EntriesFrom<SmtpEmailSender>().Where(entry => entry.Level == LogLevel.Warning).ToList();

        Assert.Equal(6, warnings.Count);
        Assert.All(warnings, warning => Assert.NotNull(warning.Exception));
        Assert.All(warnings, warning => Assert.Contains("has already been committed and is unaffected", warning.Message));
    }

    /// <summary>
    /// The failure log must identify *which* notification failed — that is the whole operational
    /// value of it until Slice 3 gives an Admin somewhere to look.
    /// </summary>
    [Fact]
    public async Task The_failure_log_identifies_the_notification_and_business_record()
    {
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(ClosedPort()), logProvider);

        await sender.SendInvoiceReadyNotificationAsync(
            new InvoiceReadyNotification(9, "RE-2026-00009", "Familie Klein", "klein@example.invalid", 1234.56m, new DateTime(2026, 8, 31), "tok-xyz789"),
            CancellationToken.None);

        var warning = AssertSingleWarning(logProvider);
        Assert.Contains(nameof(IEmailSender.SendInvoiceReadyNotificationAsync), warning.Message);
        Assert.Contains("InvoiceId=9", warning.Message);
        Assert.Contains("RE-2026-00009", warning.Message);
    }

    /// <summary>
    /// The secrecy rule holds on the *failure* path too, which is the easier one to get wrong: a
    /// diagnostic written while something is going wrong is exactly where a token tends to leak.
    /// </summary>
    [Fact]
    public async Task The_failure_log_contains_no_token_url_recipient_credentials_or_body()
    {
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(ClosedPort(), "smtp-user", "smtp-secret"), logProvider);

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        foreach (var entry in logProvider.EntriesFrom<SmtpEmailSender>())
        {
            var text = entry.Message + entry.Exception;

            Assert.DoesNotContain("tok-abc123", text);
            Assert.DoesNotContain("/angebot/", text);
            Assert.DoesNotContain("klein@example.invalid", text);
            Assert.DoesNotContain("smtp-user", text);
            Assert.DoesNotContain("smtp-secret", text);
            Assert.DoesNotContain("Guten Tag", text);
        }
    }

    [Fact]
    public async Task A_successful_delivery_produces_no_warning()
    {
        await using var server = new InProcessSmtpServer();
        var logProvider = new CapturingLoggerProvider();
        var sender = CreateSender(Options(server.Port), logProvider);

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        Assert.DoesNotContain(logProvider.EntriesFrom<SmtpEmailSender>(), entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Slice 5 owns retry. A failure must be attempted exactly once — the in-process listener records
    /// every connection, so a silently-introduced retry is directly observable rather than argued.
    /// </summary>
    [Fact]
    public async Task A_failure_is_not_retried()
    {
        await using var server = new InProcessSmtpServer(failEveryMessage: true);
        var sender = CreateSender(Options(server.Port));

        await sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None);

        Assert.Equal(1, server.SessionCount);
    }

    private static CapturedLogEntry AssertSingleWarning(CapturingLoggerProvider logProvider)
    {
        var warning = Assert.Single(
            logProvider.EntriesFrom<SmtpEmailSender>(),
            entry => entry.Level == LogLevel.Warning);

        Assert.NotNull(warning.Exception);

        return warning;
    }

    /// <summary>
    /// A port that was bound and immediately released, so nothing is listening on it — the simplest
    /// way to produce a genuine connection refusal rather than a simulated one.
    /// </summary>
    private static int ClosedPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, port: 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>D3: IsActive is not a delivery condition. A deactivated Inspector still gets told.</summary>
    [Fact]
    public async Task A_deactivated_inspector_still_receives_the_changes_requested_notification()
    {
        var inspectorId = await SeedInspectorAsync("deactivated@example.invalid", isActive: false);

        await using var server = new InProcessSmtpServer();
        var sender = CreateSender(Options(server.Port));

        await sender.SendAngebotChangesRequestedNotificationAsync(
            new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Bitte korrigieren.", inspectorId),
            CancellationToken.None);

        Assert.Contains("deactivated@example.invalid", Assert.Single(server.Messages));
    }

    private async Task<int> SeedInspectorAsync(string email, bool isActive)
    {
        await using var context = fixture.CreateContext();

        var user = new ApplicationUser
        {
            Name = "Test Inspector",
            Email = email,
            UserName = email,
            IsActive = isActive,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }
}
