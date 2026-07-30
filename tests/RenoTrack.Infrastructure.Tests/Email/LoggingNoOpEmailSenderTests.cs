using Microsoft.Extensions.Logging;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Infrastructure.Email;

namespace RenoTrack.Infrastructure.Tests.Email;

/// <summary>
/// No database involved. Proves the placeholder never throws (so Phase 2's handlers keep
/// working end-to-end) while also never being silent — a Warning-level log entry is asserted for
/// real, not just assumed from reading the code.
/// </summary>
public sealed class LoggingNoOpEmailSenderTests
{
    [Fact]
    public async Task SendNewWebsiteLeadNotificationAsync_DoesNotThrow_AndLogsAWarning()
    {
        var logger = new CapturingLogger();
        var sender = new LoggingNoOpEmailSender(logger);
        var notification = new NewWebsiteLeadNotification(1, "Jane Doe", "0176 1234567", "jane@example.com");

        await sender.SendNewWebsiteLeadNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("No email was sent", entry.Message);
        Assert.Contains("LeadId=1", entry.Message);
    }

    [Fact]
    public async Task SendAngebotSubmittedForReviewNotificationAsync_DoesNotThrow_AndLogsAWarning()
    {
        var logger = new CapturingLogger();
        var sender = new LoggingNoOpEmailSender(logger);
        var notification = new AngebotSubmittedForReviewNotification(5, "ANG-2026-00005", 1);

        await sender.SendAngebotSubmittedForReviewNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("No email was sent", entry.Message);
        Assert.Contains("ANG-2026-00005", entry.Message);
    }

    [Fact]
    public async Task SendAngebotChangesRequestedNotificationAsync_DoesNotThrow_AndLogsAWarning()
    {
        var logger = new CapturingLogger();
        var sender = new LoggingNoOpEmailSender(logger);
        var notification = new AngebotChangesRequestedNotification(5, "ANG-2026-00005", "Please adjust VAT.", 3);

        await sender.SendAngebotChangesRequestedNotificationAsync(notification, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("No email was sent", entry.Message);
    }

    /// <summary>Minimal ILogger fake capturing what was actually logged, rather than assuming it.</summary>
    private sealed class CapturingLogger : ILogger<LoggingNoOpEmailSender>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
