using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The entity's own lifecycle, with no database involved. <c>Pending → Sent</c> and
/// <c>Pending → Failed</c> are the only transitions Slice 3 produces; <c>Sending</c> does not exist
/// yet (Slice 5).
/// </summary>
public sealed class NotificationDeliveryTests
{
    private static NotificationDelivery Create() =>
        new(NotificationType.AngebotReady, "Angebot", 5);

    [Fact]
    public void A_new_delivery_starts_pending_with_one_attempt()
    {
        var before = DateTime.UtcNow;
        var delivery = Create();
        var after = DateTime.UtcNow;

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.InRange(delivery.CreatedAt, before, after);

        // The row is created for the attempt that immediately follows, so it carries an attempt
        // timestamp from birth (S3-1).
        Assert.Equal(delivery.CreatedAt, delivery.LastAttemptAt);

        Assert.Null(delivery.SentAt);
        Assert.Null(delivery.Recipient);
        Assert.Null(delivery.FailureType);
        Assert.Null(delivery.FailureMessage);
    }

    [Fact]
    public void The_business_reference_and_notification_type_are_carried_verbatim()
    {
        var delivery = new NotificationDelivery(NotificationType.InvoiceReady, "Invoice", 9);

        Assert.Equal(NotificationType.InvoiceReady, delivery.NotificationType);
        Assert.Equal("Invoice", delivery.EntityType);
        Assert.Equal(9, delivery.EntityId);
    }

    [Fact]
    public void MarkSent_records_the_send_timestamp_and_leaves_no_failure_information()
    {
        var delivery = Create();
        var sentAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        delivery.MarkSent(sentAt);

        Assert.Equal(NotificationDeliveryStatus.Sent, delivery.Status);
        Assert.Equal(sentAt, delivery.SentAt);
        Assert.Equal(sentAt, delivery.LastAttemptAt);
        Assert.Null(delivery.FailureType);
        Assert.Null(delivery.FailureMessage);
    }

    [Fact]
    public void MarkFailed_records_the_failure_and_leaves_SentAt_null()
    {
        var delivery = Create();
        var failedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        delivery.MarkFailed(failedAt, "SmtpCommandException", "The mail server could not be reached or rejected the message.");

        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("SmtpCommandException", delivery.FailureType);
        Assert.Equal("The mail server could not be reached or rejected the message.", delivery.FailureMessage);
        Assert.Equal(failedAt, delivery.LastAttemptAt);
        Assert.Null(delivery.SentAt);
    }

    /// <summary>
    /// S3-3: the recipient is recorded once known, and stays null when delivery failed before one
    /// could be resolved. Never a sentinel.
    /// </summary>
    [Fact]
    public void RecordRecipient_stores_the_resolved_address()
    {
        var delivery = Create();

        delivery.RecordRecipient("klein@example.invalid");

        Assert.Equal("klein@example.invalid", delivery.Recipient);
    }

    [Fact]
    public void A_failure_before_recipient_resolution_leaves_the_recipient_null()
    {
        var delivery = Create();

        delivery.MarkFailed(DateTime.UtcNow, "InvalidOperationException", "The notification could not be prepared.");

        Assert.Null(delivery.Recipient);
        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
    }

    /// <summary>Slice 5 owns <c>Sending</c>; Slice 3 must not have introduced it.</summary>
    [Fact]
    public void The_status_enum_has_exactly_the_three_slice_3_states()
    {
        Assert.Equal(
            ["Pending", "Sent", "Failed"],
            Enum.GetNames<NotificationDeliveryStatus>());
    }

    /// <summary>One value per IEmailSender method — no more, no fewer.</summary>
    [Fact]
    public void The_notification_type_enum_covers_exactly_the_six_notifications()
    {
        Assert.Equal(
            [
                "NewWebsiteLead",
                "AngebotSubmittedForReview",
                "AngebotChangesRequested",
                "AngebotReady",
                "InvoiceReady",
                "AngebotDecision",
            ],
            Enum.GetNames<NotificationType>());
    }
}
