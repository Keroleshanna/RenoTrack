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
/// <b>The invoice PDF travels as <paramref name="InvoicePdf"/> (Phase 14 Slice 3a, D113)</b> —
/// FR-8.3's "a token link, by email as a PDF, or both", now both. These are the archived document's
/// bytes, never a second rendering: the send handler passes the very array it wrote to storage, and
/// a retry reads the archive back. <see langword="null"/> only when an invoice sent before Slice 3a
/// is retried — it has no archived document and keeps the link-only email it always had.
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
    string Token,
    byte[]? InvoicePdf);
