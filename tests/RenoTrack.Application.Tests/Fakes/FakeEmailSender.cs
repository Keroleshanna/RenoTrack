using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeEmailSender : IEmailSender
{
    public List<NewWebsiteLeadNotification> NewWebsiteLeadNotifications { get; } = [];
    public List<AngebotSubmittedForReviewNotification> AngebotSubmittedForReviewNotifications { get; } = [];
    public List<AngebotChangesRequestedNotification> AngebotChangesRequestedNotifications { get; } = [];

    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken)
    {
        NewWebsiteLeadNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken)
    {
        AngebotSubmittedForReviewNotifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken)
    {
        AngebotChangesRequestedNotifications.Add(notification);
        return Task.CompletedTask;
    }
}
