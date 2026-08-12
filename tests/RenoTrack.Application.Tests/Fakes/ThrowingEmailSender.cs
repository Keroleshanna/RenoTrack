using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// An <see cref="IEmailSender"/> whose every method throws.
///
/// <para><b>Why this exists, given the real sender never throws after Phase 9 Slice 2.</b> That is
/// precisely the point: the failure boundary is Infrastructure's, and this fake proves the
/// Application layer did not quietly acquire one of its own. If a handler ever grew a
/// <c>try</c>/<c>catch</c> around its notification call, the test using this fake would start
/// passing for the wrong reason — so it asserts the exception <em>does</em> reach the caller,
/// pinning that handlers stay thin (CLAUDE.md §6) and that the boundary stays in one place.</para>
///
/// <para>Deliberately a separate type rather than a flag on <see cref="FakeEmailSender"/>: that fake
/// records notifications for a dozen existing tests, and giving it a throwing mode would put a
/// behaviour switch inside the thing they rely on.</para>
/// </summary>
public sealed class ThrowingEmailSender : IEmailSender
{
    public sealed class DeliveryFailedException(string method)
        : Exception($"Simulated delivery failure in {method}.");

    public Task SendNewWebsiteLeadNotificationAsync(NewWebsiteLeadNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendNewWebsiteLeadNotificationAsync));

    public Task SendAngebotSubmittedForReviewNotificationAsync(AngebotSubmittedForReviewNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendAngebotSubmittedForReviewNotificationAsync));

    public Task SendAngebotChangesRequestedNotificationAsync(AngebotChangesRequestedNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendAngebotChangesRequestedNotificationAsync));

    public Task SendAngebotReadyNotificationAsync(AngebotReadyNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendAngebotReadyNotificationAsync));

    public Task SendInvoiceReadyNotificationAsync(InvoiceReadyNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendInvoiceReadyNotificationAsync));

    public Task SendAngebotDecisionNotificationAsync(AngebotDecisionNotification notification, CancellationToken cancellationToken) =>
        throw new DeliveryFailedException(nameof(SendAngebotDecisionNotificationAsync));
}
