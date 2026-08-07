using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Domain.Entities;

/// <summary>
/// The actual job, once the customer has agreed to the Angebot (BR-2: "A Project represents
/// committed, paid work"). Architecture.md §6: "Project (root) — references Customer, Angebot by
/// id." Owns no children; Invoices reference a Project by id and arrive in Phase 8.
///
/// <para>
/// <b>BR-2's guard is not here, and that is deliberate.</b> "Only a <c>CustomerApproved</c>
/// Angebot may become a Project" is a statement about a <i>different</i> aggregate's state, which
/// CLAUDE.md §2's self-guard rule puts out of reach: this type has no <c>Angebot</c> reference and
/// must not acquire one. BR-2 assigns enforcement to <c>ConvertAngebotToProjectCommand</c> by
/// name, and StateMachine.md §5 repeats it. The Application layer therefore validates the
/// Angebot's status and passes only the values below — including an <see cref="AgreedTotal"/>
/// already derived from <c>Angebot.GrossTotal</c>. Do not "fix" this by passing an Angebot into
/// <see cref="Create"/>; the decoupling is the point.
/// </para>
/// <para>
/// <b><see cref="AgreedTotal"/> is a snapshot, never a live read.</b> ERD.md: "AgreedTotal is a
/// snapshot of Angebot.GrossTotal at conversion time (doesn't move if the Angebot were ever
/// re-opened)". It is assigned once at creation and no method mutates it — the same
/// copy-at-creation guarantee BR-8 gives <c>AngebotItem</c>, structurally rather than by
/// convention.
/// </para>
/// </summary>
public sealed class Project
{
    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public int AngebotId { get; private set; }
    public ProjectStatus Status { get; private set; }
    public Money AgreedTotal { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Assignment only — every guard lives in <see cref="Create"/> (CLAUDE.md §2), so nothing here
    /// re-runs when EF Core materialises a persisted row through this same constructor.
    /// </summary>
    private Project(int customerId, int angebotId, Money agreedTotal)
    {
        CustomerId = customerId;
        AngebotId = angebotId;
        AgreedTotal = agreedTotal;
        Status = ProjectStatus.Active;
        CreatedAt = DateTime.UtcNow;
        CompletedAt = null;
    }

    /// <summary>
    /// StateMachine.md §4.3: <c>[*] --ConvertAngebotToProject--> Active</c>. A Project is born
    /// <see cref="ProjectStatus.Active"/> — §4.2's diagram has no other entry point.
    ///
    /// <para>
    /// The guards are self-guards only. That <paramref name="angebotId"/> names a real,
    /// <c>CustomerApproved</c>, not-yet-converted Angebot is checked by the Application layer
    /// (BR-2); that <paramref name="customerId"/> names a real Customer is checked there and by
    /// the foreign key. A negative <see cref="AgreedTotal"/> is refused because no reading of the
    /// documents makes a Project agreed at less than nothing meaningful — and unlike the two ids,
    /// it needs no external knowledge to reject. Zero is allowed: <c>Money.Zero</c> is a legal
    /// <c>Angebot.GrossTotal</c>, and refusing it would invent a minimum-value rule.
    /// </para>
    /// </summary>
    public static Project Create(int customerId, int angebotId, Money agreedTotal)
    {
        if (customerId <= 0)
            throw new ArgumentException("Customer id must be positive.", nameof(customerId));
        if (angebotId <= 0)
            throw new ArgumentException("Angebot id must be positive.", nameof(angebotId));
        ArgumentNullException.ThrowIfNull(agreedTotal);
        if (agreedTotal.Amount < 0)
            throw new ArgumentException("Agreed total cannot be negative.", nameof(agreedTotal));

        return new Project(customerId, angebotId, agreedTotal);
    }

    /// <summary>
    /// StateMachine.md §4.3: <c>Active → OnHold</c>, no guard beyond the source state.
    /// The table's "Reason optional/free text" note describes a field no ERD column exists for,
    /// so none is accepted here — adding one would invent storage the documents do not define.
    /// </summary>
    public void PutOnHold()
    {
        EnsureStatus(ProjectStatus.Active, nameof(PutOnHold));
        Status = ProjectStatus.OnHold;
    }

    /// <summary>
    /// StateMachine.md §4.3: <c>OnHold → Active</c>, no guard beyond the source state.
    /// </summary>
    public void Resume()
    {
        EnsureStatus(ProjectStatus.OnHold, nameof(Resume));
        Status = ProjectStatus.Active;
    }

    /// <summary>
    /// StateMachine.md §4.3: <c>Active → Completed</c>. Terminal — §4.2's diagram gives
    /// <c>Completed</c> no outgoing transition, so a completed Project can never be reopened,
    /// put on hold, or completed twice.
    ///
    /// <para>
    /// <b>Only from <see cref="ProjectStatus.Active"/>, not from <see cref="ProjectStatus.OnHold"/>.</b>
    /// §4.2's diagram draws <c>Active --> Completed</c> and nothing from <c>OnHold</c> except
    /// <c>Resume</c>; a paused Project is resumed first. That is the documented shape, so it is
    /// the shape encoded here rather than the more permissive one.
    /// </para>
    /// <para>
    /// <b>The invoice precondition is not enforced here, and cannot be.</b> §4.3 guards this
    /// transition on "All Invoices.Status == Paid (or Void)", with an Admin override path
    /// (FR-8.6). Both require knowledge of a different aggregate, which CLAUDE.md §2 places
    /// outside this type — and Invoices do not exist yet. <c>CompleteProjectCommand</c> (Phase 8)
    /// owns that check and the override, exactly as StateMachine.md §5 says. This method enforces
    /// only Project's own state invariant.
    /// </para>
    /// </summary>
    public void Complete()
    {
        EnsureStatus(ProjectStatus.Active, nameof(Complete));
        Status = ProjectStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    private void EnsureStatus(ProjectStatus expected, string transitionName)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{transitionName}': Project {Id} is in status '{Status}', expected '{expected}'.");
        }
    }
}
