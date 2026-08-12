namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// Which of the six <c>IEmailSender</c> notifications a delivery record describes.
///
/// <para><b>Infrastructure-owned, unlike <c>AuditAction</c>.</b> That enum lives in
/// <c>Application.Common</c> because Application handlers pass it; this one is only ever known
/// inside <see cref="Email.SmtpEmailSender"/>, so placing it in Application would create the new
/// Application notification abstraction Phase 9 Slice 3 explicitly forbids.</para>
///
/// <para>Stored as a string (D69, <c>CLAUDE.md</c> §21) — readable in raw SQL during support, and
/// adding a member later needs no migration. Values are named for the notification rather than the
/// method, so a row reads correctly on its own.</para>
/// </summary>
public enum NotificationType
{
    /// <summary>SRS FR-9.2 — a new Lead arrived through the website contact form.</summary>
    NewWebsiteLead,

    /// <summary>SRS FR-9.2 — an Inspector submitted an Angebot for internal review.</summary>
    AngebotSubmittedForReview,

    /// <summary>Sequence Diagram §5 — an Admin requested changes; the only Inspector-facing notification.</summary>
    AngebotChangesRequested,

    /// <summary>SRS FR-9.1 — the customer's Angebot token link.</summary>
    AngebotReady,

    /// <summary>SRS FR-9.1 — the customer's Invoice token link.</summary>
    InvoiceReady,

    /// <summary>SRS FR-9.2 — the customer approved or rejected an Angebot.</summary>
    AngebotDecision,
}
