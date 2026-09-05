namespace RenoTrack.Application.Angebote.Commands.ResendAngebot;

/// <summary>
/// SRS FR-6.1a / <b>D99</b>. Re-issues the customer's token link for an Angebot that is still
/// awaiting a decision — a lost email, a link that lapsed before the Lead answered, or a corrected
/// address.
///
/// <para>
/// <b>No state transition.</b> The Angebot stays <c>Sent</c>, the Lead stays <c>AngebotSent</c>,
/// and <c>SentAt</c> is deliberately unchanged: it records the original send, and each re-issue
/// appears in the audit trail instead.
/// </para>
/// <para>
/// <b>The old link is superseded, never deleted and never marked used.</b> It is expired in the
/// same transaction that creates the replacement, so at most one credential is ever usable.
/// </para>
/// </summary>
/// <param name="ResentByAdminId">
/// Who acted, from the JWT rather than the body (D61). Used for the audit entry only — sending is
/// an <c>F</c> permission (PermissionMatrix §4), so it scopes nothing.
/// </param>
public sealed record ResendAngebotCommand(int AngebotId, int ResentByAdminId);
