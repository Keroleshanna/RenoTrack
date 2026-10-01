using RenoTrack.Domain.Enums;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// Aggregate root at the top of the customer journey (Architecture.md §6). Owns no child
/// entities directly — Inspection and Angebot reference a Lead by id, not the other way
/// around, per Architecture §6: "Lead (root) — owns nothing directly beyond its own fields;
/// references Inspection/Angebot by id."
///
/// Status only ever changes through the named methods below (BR-7: "never silently or as a
/// side effect of an unrelated operation"). This aggregate deliberately has zero knowledge of
/// Inspection or Angebot as types — when an action on one of those aggregates should also
/// move this Lead forward (e.g. an Angebot reaching Sent), the Application layer is
/// responsible for calling the matching method here after that other aggregate's own
/// operation succeeds. That keeps each aggregate's invariants owned by itself, with
/// cross-aggregate coordination living one layer up, not as direct coupling between
/// aggregates.
/// </summary>
public sealed class Lead
{
    /// <summary>
    /// The maximum lengths of a Lead's text fields — one definition, read by the Application layer's
    /// validator and by the EF Core configuration that creates the columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>They live here because the entity owns them, and because two definitions drift.</b> Before
    /// Phase 13 Slice 7 the lengths existed only as literals in the schema, so an over-long value
    /// passed every guard and failed at the database: a plain bad request surfaced as a 500 with a
    /// stack trace rather than a field-keyed 400. The validator, the Domain guard and the column now
    /// read the same constants, so no layer can disagree with another about what fits.
    /// </para>
    /// <para>
    /// <b>A maximum length is a lifetime invariant</b> (CLAUDE.md §2): a stored row can never exceed
    /// its own column, so checking it in <see cref="Create"/> can never make a persisted Lead
    /// unreadable — unlike a time-dependent condition, which is why these guards sit in the factory
    /// with the rest and never in the constructor EF Core calls.
    /// </para>
    /// </remarks>
    public const int MaxNameLength = 200;

    /// <inheritdoc cref="MaxNameLength"/>
    public const int MaxPhoneLength = 50;

    /// <summary>320 characters: the longest address RFC 5321 allows (64 local + @ + 255 domain).</summary>
    public const int MaxEmailLength = 320;

    /// <inheritdoc cref="MaxNameLength"/>
    public const int MaxAddressLength = 500;

    /// <inheritdoc cref="MaxNameLength"/>
    public const int MaxNotesLength = 2000;

    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Phone { get; private set; }
    public string Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public LeadSource Source { get; private set; }
    public LeadStatus Status { get; private set; }
    public int? AssignedInspectorId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Lead(string name, string phone, string email, string? address, string? notes, LeadSource source)
    {
        Name = name;
        Phone = phone;
        Email = email;
        Address = address;
        Notes = notes;
        Source = source;
        Status = LeadStatus.New;
        AssignedInspectorId = null;
        CreatedAt = DateTime.UtcNow;
    }

    private static void EnsureFits(string? value, int maximumLength, string parameterName)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Lead {parameterName} must be at most {maximumLength} characters.",
                parameterName);
        }
    }

    /// <summary>
    /// Creates a new Lead in the <see cref="LeadStatus.New"/> state. Covers both creation
    /// paths in Sequence Diagram.md — §1 (public website form, Address typically absent) and
    /// §2 (Admin manual entry, Address typically present) — by treating <paramref name="address"/>
    /// and <paramref name="notes"/> as optional at the Domain level; per-channel field
    /// requirements (e.g. "Address is required for manual entry") belong in the Application
    /// layer's validators (Architecture.md §5.1), not here.
    /// </summary>
    public static Lead Create(string name, string phone, string email, LeadSource source, string? address = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Lead name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Lead phone is required.", nameof(phone));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Lead email is required.", nameof(email));

        // Measured after trimming, because trimming is what the stored value will be.
        var trimmedName = name.Trim();
        var trimmedPhone = phone.Trim();
        var trimmedEmail = email.Trim();
        var trimmedAddress = address?.Trim();
        var trimmedNotes = notes?.Trim();

        EnsureFits(trimmedName, MaxNameLength, nameof(name));
        EnsureFits(trimmedPhone, MaxPhoneLength, nameof(phone));
        EnsureFits(trimmedEmail, MaxEmailLength, nameof(email));
        EnsureFits(trimmedAddress, MaxAddressLength, nameof(address));
        EnsureFits(trimmedNotes, MaxNotesLength, nameof(notes));

        return new Lead(trimmedName, trimmedPhone, trimmedEmail, trimmedAddress, trimmedNotes, source);
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>New → InspectionScheduled</c>.
    /// Self-guard (owned by this aggregate): <see cref="Status"/> must currently be <see cref="LeadStatus.New"/>.
    /// Application-layer precondition (not enforced here — this aggregate has no way to check
    /// it): none beyond the self-guard for this particular transition; the documented guard
    /// ("Lead exists") is trivially satisfied by the caller already holding this instance.
    /// </summary>
    public void MarkInspectionScheduled()
    {
        EnsureStatus(LeadStatus.New, nameof(MarkInspectionScheduled));
        Status = LeadStatus.InspectionScheduled;
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>InspectionScheduled → InspectionDone</c>.
    /// Self-guard: <see cref="Status"/> must currently be <see cref="LeadStatus.InspectionScheduled"/>.
    /// Application-layer precondition (not enforced here): the Inspection being completed
    /// actually belongs to this Lead (<c>Inspection.LeadId == this.Id</c>). This aggregate has
    /// zero knowledge of the Inspection entity (Architecture.md §6), so it structurally cannot
    /// check that itself.
    /// </summary>
    public void MarkInspectionDone()
    {
        EnsureStatus(LeadStatus.InspectionScheduled, nameof(MarkInspectionDone));
        Status = LeadStatus.InspectionDone;
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>InspectionDone → AngebotInProgress</c>.
    /// Self-guard: <see cref="Status"/> must currently be <see cref="LeadStatus.InspectionDone"/>.
    /// Application-layer precondition (not enforced here): no other non-terminal Angebot
    /// already exists for this Lead (StateMachine.md §2.4). Verifying that requires querying
    /// the Angebot aggregate's repository — a cross-aggregate check this Lead cannot perform
    /// on its own.
    /// </summary>
    public void MarkAngebotInProgress()
    {
        EnsureStatus(LeadStatus.InspectionDone, nameof(MarkAngebotInProgress));
        Status = LeadStatus.AngebotInProgress;
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>AngebotInProgress → AngebotSent</c>.
    /// Self-guard: <see cref="Status"/> must currently be <see cref="LeadStatus.AngebotInProgress"/>.
    /// Application-layer precondition (not enforced here): the associated Angebot has actually
    /// reached <c>AngebotStatus.Sent</c>. By the time the Application layer calls this method,
    /// that will already be true — it is the direct result of the <c>SendAngebotCommand</c>
    /// having just succeeded on the Angebot aggregate, so there is nothing left to re-check.
    /// </summary>
    public void MarkAngebotSent()
    {
        EnsureStatus(LeadStatus.AngebotInProgress, nameof(MarkAngebotSent));
        Status = LeadStatus.AngebotSent;
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>AngebotSent → Won</c> (terminal).
    /// Self-guard: <see cref="Status"/> must currently be <see cref="LeadStatus.AngebotSent"/>.
    /// Application-layer precondition (not enforced here): the customer's token link was valid
    /// and unused at decision time (BR-4, Sequence Diagram §12 — <c>ValidateTokenLinkHandler</c>).
    /// This aggregate has no concept of TokenLink at all.
    /// </summary>
    public void MarkWon()
    {
        EnsureStatus(LeadStatus.AngebotSent, nameof(MarkWon));
        Status = LeadStatus.Won;
    }

    /// <summary>
    /// StateMachine.md §1.3: <c>AngebotSent → Lost</c> (terminal).
    /// Self-guard: <see cref="Status"/> must currently be <see cref="LeadStatus.AngebotSent"/>.
    /// Application-layer precondition (not enforced here): same as <see cref="MarkWon"/> — a
    /// valid, unused token link at decision time.
    /// </summary>
    public void MarkLost()
    {
        EnsureStatus(LeadStatus.AngebotSent, nameof(MarkLost));
        Status = LeadStatus.Lost;
    }

    /// <summary>
    /// Corrects the Lead's contact details. PermissionMatrix.md §1: "Edit Lead contact details —
    /// Admin F, Inspector S", whose own example is an Inspector fixing a wrong phone number found
    /// on-site.
    ///
    /// <para>
    /// <b>Scoped to exactly the four contact fields, and <see cref="Notes"/> is deliberately not
    /// among them.</b> §1 grants editing of "contact details"; FR-2.1 lists notes separately from
    /// name/phone/email/address, and no document grants an edit of the enquiry description itself.
    /// Including it here would widen a documented permission by assumption rather than by
    /// evidence — the same discipline CLAUDE.md §2 applies to child-entity update methods. That
    /// leaves Lead notes writable only at creation, which is recorded as a known gap rather than
    /// quietly closed here.
    /// </para>
    /// <para>
    /// <b>No <see cref="LeadStatus"/> guard, for the same reason <see cref="AssignInspector"/> has
    /// none.</b> This is clerical correction, not a lifecycle transition: StateMachine.md defines
    /// no event for it and PermissionMatrix.md places no timing restriction on it, so refusing it
    /// in (say) a terminal state would invent a rule no document states. A wrong phone number is
    /// worth fixing on a <c>Won</c> Lead too — the Customer record copied at conversion time is
    /// unaffected either way, which is exactly the point of BR-8-style copy-at-commit.
    /// </para>
    ///
    /// The guards mirror <see cref="Create"/>'s, because the same three fields are required for
    /// the whole of the Lead's lifetime — an invariant, not a creation-time condition, so it is
    /// legitimate for both to enforce it.
    /// </summary>
    public void UpdateContactDetails(string name, string phone, string email, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Lead name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Lead phone is required.", nameof(phone));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Lead email is required.", nameof(email));

        Name = name.Trim();
        Phone = phone.Trim();
        Email = email.Trim();
        Address = address?.Trim();
    }

    /// <summary>
    /// Assigns (or reassigns) which Inspector is responsible for this Lead. PermissionMatrix.md
    /// §1: "Assign/reassign Inspector to a Lead — Admin decision."
    ///
    /// Deliberately has no <see cref="LeadStatus"/> guard and never changes <see cref="Status"/>.
    /// This is an administrative action, not a lifecycle transition — neither StateMachine.md
    /// nor PermissionMatrix.md places any restriction on when it may happen, so adding a status
    /// guard here would be inventing a new business rule rather than encoding an existing one.
    /// See Architecture.md §6 for the same rationale recorded at the architecture-document
    /// level, so this is legible as an intentional choice, not an oversight.
    /// </summary>
    public void AssignInspector(int inspectorId)
    {
        AssignedInspectorId = inspectorId;
    }

    private void EnsureStatus(LeadStatus expected, string transitionName)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{transitionName}': Lead {Id} is in status '{Status}', expected '{expected}'.");
        }
    }
}
