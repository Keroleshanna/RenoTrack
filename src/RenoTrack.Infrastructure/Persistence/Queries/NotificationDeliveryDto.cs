using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// One row of the Admin's notification-delivery list (`PermissionMatrix.md` §9, "View failed/pending
/// notifications").
///
/// <para><b>Why this DTO lives in Infrastructure rather than Application</b>, unlike every other DTO
/// in this codebase (<c>CLAUDE.md</c> §7): its two enums are Infrastructure types by D69, and
/// Application cannot reference Infrastructure. Moving them down would create exactly the
/// Application notification-persistence abstraction D69 forbids, and copying them would give one
/// persisted string column two sources of truth. So the read stays wholly on the side that owns the
/// record — the same reasoning D60 applies to authentication, where the absence of any business rule
/// is what makes bypassing the Application layer correct rather than expedient.</para>
///
/// <para><b>Every persisted column is exposed, deliberately.</b> Nothing here is a secret by
/// construction: D69 already guarantees the row carries no token, no rendered subject or body, no
/// credential and no raw exception text, and <see cref="FailureMessage"/> was sanitized at write
/// time. Withholding fields would only defeat the triage this endpoint exists for — and a second
/// sanitization pass here would imply the stored value is untrusted, which would be the wrong lesson
/// to teach about a column the sender is responsible for keeping clean.</para>
/// </summary>
/// <param name="Recipient">
/// The complete resolved recipient set, joined by <see cref="NotificationDelivery.RecipientSeparator"/>.
/// <b>Null is a real answer</b>, not missing data: delivery failed before a recipient could be
/// resolved. It is projected and serialized as <c>null</c> — never an empty string and never a
/// sentinel, for the reason the entity itself records.
/// </param>
/// <param name="EntityType">
/// <c>Lead</c>, <c>Angebot</c> or <c>Invoice</c>, exactly as stored. Deliberately not resolved to a
/// title, number or link: <c>EntityId</c> is polymorphic across three tables with no foreign key to
/// join through, and no document asks for a resolved label. This is an operational view of the
/// delivery record, not a report about the business object.
/// </param>
/// <param name="AttemptCount">
/// Always <c>1</c> today, because nothing retries yet (D70's retry is Slice 5). A real historical
/// fact rather than scaffolding, so it is reported rather than hidden.
/// </param>
public sealed record NotificationDeliveryDto(
    int Id,
    NotificationType NotificationType,
    string EntityType,
    int EntityId,
    NotificationDeliveryStatus Status,
    string? Recipient,
    DateTime CreatedAt,
    DateTime? LastAttemptAt,
    int AttemptCount,
    DateTime? SentAt,
    string? FailureType,
    string? FailureMessage);
