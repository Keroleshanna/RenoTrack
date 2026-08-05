using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// A single-purpose, expiring, unguessable link handed to a customer who has no account
/// (Architecture.md §7.2, SRS FR-6.1). Independent aggregate root — Architecture.md §6 lists
/// "TokenLink (root) — polymorphic reference to Angebot or Invoice via EntityType + EntityId",
/// so it relates to what it points at by id only and has no navigation property to Angebot or
/// Invoice as a type. ERD.md records the same intent on the storage side: no database FK, since
/// no single column can reference two different tables.
///
/// This is a Domain entity rather than an Infrastructure-only persistence model (the treatment
/// AuditLog got in D49, NumberSequence in D51, RefreshToken in Phase 4) because it carries a
/// numbered business rule of its own: BR-4 ("a token link is single-use for decisions") is
/// enforced by <see cref="MarkUsed"/> here, not by a handler's discipline. Those three types
/// were classified as Infrastructure precisely because no business invariant referenced them;
/// this one is referenced by BR-4, StateMachine.md §2.3's decision guards, and Architecture.md
/// §6's own aggregate list.
///
/// The token itself is generated outside this type, by <c>ITokenLinkService</c> — the same split
/// as <c>INumberGeneratorService</c> and <c>Angebot.AngebotNumber</c>: the Domain owns what makes
/// a value valid, Infrastructure owns how it is produced.
/// </summary>
public sealed class TokenLink
{
    public int Id { get; private set; }
    public TokenLinkEntityType EntityType { get; private set; }
    public int EntityId { get; private set; }
    public string Token { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TokenLink(TokenLinkEntityType entityType, int entityId, string token, DateTime expiresAt)
    {
        if (entityId <= 0)
            throw new ArgumentException("Entity id must be positive.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token is required.", nameof(token));

        var createdAt = DateTime.UtcNow;

        // A link that is already expired the moment it exists can never be used for anything, so
        // constructing one is a caller bug rather than a state the system should be able to hold.
        // This is a self-guard in the CLAUDE.md §2 sense — determinable from the arguments and the
        // clock alone, with no other aggregate or repository involved.
        if (expiresAt <= createdAt)
            throw new ArgumentException("Expiry must be in the future.", nameof(expiresAt));

        EntityType = entityType;
        EntityId = entityId;
        Token = token;
        ExpiresAt = expiresAt;
        UsedAt = null;
        CreatedAt = createdAt;
    }

    public static TokenLink Create(TokenLinkEntityType entityType, int entityId, string token, DateTime expiresAt) =>
        new(entityType, entityId, token, expiresAt);

    /// <summary>
    /// Whether this link's validity window has closed, evaluated against a caller-supplied clock
    /// reading rather than <see cref="DateTime.UtcNow"/> directly.
    ///
    /// This is deliberately a public read, which D29 rejected for <c>Inspection.IsEditable</c> —
    /// the distinction is that D29's property existed only so a handler could avoid calling a
    /// mutator that would have thrown anyway, whereas the public *read* endpoint
    /// (<c>GET /api/v1/public/angebote/{token}</c>) mutates nothing at all, so there is no guard
    /// for it to reject through. Sequence Diagram §12 requires expiry to be checked on that path,
    /// and without this method the check would have to be re-derived from <see cref="ExpiresAt"/>
    /// by every caller — the drift CLAUDE.md §2 exists to prevent.
    ///
    /// The <paramref name="asOf"/> parameter keeps the rule deterministic under test: expiry can
    /// be exercised by moving the reading rather than by sleeping or by reflecting into
    /// <see cref="ExpiresAt"/> to backdate it.
    /// </summary>
    public bool IsExpired(DateTime asOf) => asOf >= ExpiresAt;

    /// <summary>
    /// BR-4: once a decision has been recorded through this link, the same link can never carry
    /// another state-changing action. Viewing is unaffected — BR-4 says so explicitly, and
    /// Sequence Diagram §12 scopes the <see cref="UsedAt"/> check to "decision-type actions only".
    ///
    /// Guards expiry as well as prior use: both are facts about this aggregate's own state, so
    /// CLAUDE.md §2 puts both here rather than trusting a caller to have checked first. The
    /// Application layer still checks them ahead of this call, because it must distinguish the two
    /// failures to produce the right HTTP status (Sequence Diagram §12's "Gone / Forbidden
    /// (specific reason)") — that is a presentation concern, not a duplicated invariant.
    /// </summary>
    public void MarkUsed()
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException(
                $"Token link {Id} was already used at {UsedAt:O} and cannot be used again (BR-4).");
        }

        var now = DateTime.UtcNow;
        if (IsExpired(now))
        {
            throw new InvalidOperationException(
                $"Token link {Id} expired at {ExpiresAt:O} and cannot be used.");
        }

        UsedAt = now;
    }
}
