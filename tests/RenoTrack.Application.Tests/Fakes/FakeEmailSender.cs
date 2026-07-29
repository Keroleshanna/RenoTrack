using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeEmailSender : IEmailSender
{
    public List<NewWebsiteLeadNotification> NewWebsiteLeadNotifications { get; } = [];

    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken)
    {
        NewWebsiteLeadNotifications.Add(notification);
        return Task.CompletedTask;
    }
}
