using Microsoft.Extensions.Logging;
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
    /// Slice 1's defining behaviour: a transport failure reaches the caller. Slice 2 turns this into
    /// catch/log/never-rethrow; until then it must not be swallowed anywhere.
    /// </summary>
    [Fact]
    public async Task A_refused_connection_propagates_rather_than_being_swallowed()
    {
        // Bind and immediately release a port so nothing is listening on it.
        int closedPort;
        await using (var server = new InProcessSmtpServer())
        {
            closedPort = server.Port;
        }

        var sender = CreateSender(Options(closedPort));

        await Assert.ThrowsAnyAsync<Exception>(
            () => sender.SendAngebotReadyNotificationAsync(AngebotReady, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        await using var server = new InProcessSmtpServer();
        var sender = CreateSender(Options(server.Port));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAngebotReadyNotificationAsync(AngebotReady, cancellation.Token));
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

    /// <summary>D2: a missing Inspector address is a delivery failure, never a silent skip.</summary>
    [Fact]
    public async Task A_missing_inspector_address_fails_rather_than_skipping_silently()
    {
        await using var server = new InProcessSmtpServer();
        var sender = CreateSender(Options(server.Port));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAngebotChangesRequestedNotificationAsync(
                new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Bitte korrigieren.", InspectorId: 999_999),
                CancellationToken.None));

        Assert.Contains("999999", exception.Message);
        Assert.Empty(server.Messages);
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
