namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// The delivery lifecycle Slice 3 can actually produce: <c>Pending → Sent</c> or
/// <c>Pending → Failed</c>.
///
/// <para><b><c>Sending</c> is deliberately absent.</b> D69 names it, but its only purpose (D70) is
/// Slice 5's guard against an Admin double-clicking retry — and Slice 3 is explicitly forbidden from
/// solving concurrency. Because this enum is stored as a string, adding the member in Slice 5 costs
/// no migration, which is what makes deferring it free rather than merely tidy.</para>
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>The row exists and an attempt is under way. A row left here means the process died mid-attempt.</summary>
    Pending,

    /// <summary>The SMTP server accepted the message.</summary>
    Sent,

    /// <summary>Preparation, transport or cancellation ended the attempt. Not retried automatically (D70).</summary>
    Failed,
}
