namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// The delivery lifecycle as of Slice 5:
///
/// <code>
/// Pending → Sending → Sent | Failed
/// Failed  → Sending → Sent | Failed
/// Pending → Sending            (manual recovery of a stranded initial attempt)
/// Sending → Sending            (manual recovery of a stranded retry attempt)
/// </code>
///
/// <para><b><c>Sending</c> arrived in Slice 5, and adding it cost no migration</b> — the column is
/// string-converted with room to spare, which is exactly why Slice 3 could defer it for free rather
/// than merely tidily.</para>
///
/// <para><b>Nothing here is a lease.</b> There is no worker, no timeout and no automatic recovery
/// anywhere in this system (D69/D70), so a process that dies mid-attempt strands a row in
/// <see cref="Sending"/> permanently. That is precisely why <see cref="Sending"/> is itself
/// retryable: the only thing that can rescue such a row is an Admin clicking retry again. Do not
/// "improve" this with a timeout sweep — that would be the background mechanism D69 rules out.</para>
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>
    /// The row exists and the original attempt is under way. A row left here means the process died
    /// mid-attempt, or the terminal write itself failed. Retryable.
    /// </summary>
    Pending,

    /// <summary>The SMTP server accepted the message. <b>Terminal, and never retryable.</b></summary>
    Sent,

    /// <summary>Preparation, transport or cancellation ended the attempt. Never retried automatically (D70), retryable by hand.</summary>
    Failed,

    /// <summary>
    /// Claimed by a retry that is in flight. Written only by the atomic compare-and-set claim
    /// (S5-2), which is what makes an Admin double-click impossible to double-send: the second
    /// request matches no eligible row and is refused with 409.
    /// </summary>
    Sending,
}
