namespace RenoTrack.Application.Common.Notifications;

/// <summary>
/// SRS FR-9.1 / Sequence Diagram §9: email the customer their token link once an Invoice is sent.
/// FR-9.1 names Angebot **and Invoice** in the same sentence, and this is the Invoice half —
/// structurally the twin of <see cref="AngebotReadyNotification"/>.
///
/// <para>
/// Carries the raw <paramref name="Token"/> rather than a finished URL, for the same reason: the
/// public website's base address is deployment configuration, and Application deliberately takes no
/// <c>IConfiguration</c> at all (CLAUDE.md §22). Phase 9 owns both the German template and the base
/// URL it is interpolated into.
/// </para>
/// <para>
/// <b>No PDF attachment field.</b> Sequence Diagram §9 draws one, but PDF generation is Phase 14's
/// and no abstraction for it exists (approved Phase 8 decision G-4). FR-8.3 permits "a token link,
/// by email as a PDF, or both" — this is the link. A field for an attachment nothing can produce
/// would be a speculative contract.
/// </para>
/// <para>
/// <b>No bank details either</b>, which Wireframe A4 renders next to the payment instructions: no
/// document defines where the company's IBAN/BIC live, and inventing a configuration surface for
/// them is not this slice's to do (G-5).
/// </para>
/// </summary>
/// <param name="RecipientEmail">
/// The Customer's own address — Sequence Diagram §9 sends <c>to=Customer.Email</c>. Present for the
/// same reason <see cref="AngebotReadyNotification"/> carries one: this notification goes to the
/// customer, not to "whoever the Admin mailbox is".
/// </param>
public sealed record InvoiceReadyNotification(
    int InvoiceId,
    string InvoiceNumber,
    string RecipientName,
    string RecipientEmail,
    decimal GrossAmount,
    DateTime DueDate,
    string Token);
