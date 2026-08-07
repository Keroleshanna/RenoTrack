namespace RenoTrack.Application.Common.Notifications;

/// <summary>
/// SRS FR-9.1 / Sequence Diagram §6: email the Lead their token link once an Angebot is sent. The
/// first customer-facing notification in the system — every prior one (FR-9.2) is Admin- or
/// Inspector-facing.
///
/// Carries the raw <paramref name="Token"/> rather than a finished URL. Composing
/// "https://…/angebot/{token}" needs the public website's base address, which is deployment
/// configuration: Application knowing it would put a hosting concern in the layer that
/// deliberately takes no IConfiguration at all (CLAUDE.md §22). The Phase 9 implementation owns
/// both the German template and the base URL it is interpolated into.
/// </summary>
/// <param name="RecipientEmail">
/// The Lead's own address. Included because this notification goes to the customer, unlike the
/// FR-9.2 notifications whose recipient is "whoever the Admin mailbox is" and therefore needs no
/// address in the model.
/// </param>
public sealed record AngebotReadyNotification(
    int AngebotId,
    string AngebotNumber,
    string RecipientName,
    string RecipientEmail,
    string Token);
