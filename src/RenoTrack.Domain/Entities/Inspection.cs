namespace RenoTrack.Domain.Entities;

/// <summary>
/// Aggregate root for an on-site visit (Architecture.md §6) → InspectionPhoto (child).
/// References <see cref="LeadId"/> and <see cref="InspectorId"/> by id only, with no
/// navigation properties — this aggregate has zero compile-time knowledge of the Lead or
/// User types, matching how Lead has zero knowledge of Inspection/Angebot.
///
/// Unlike Lead/Angebot, StateMachine.md defines no separate state machine section for
/// Inspection: its only real state is the binary captured by <see cref="CompletedAt"/>
/// (null = not yet completed, set = completed). No separate status enum is introduced here —
/// that single nullable timestamp is the one source of truth.
///
/// BR-10: once completed, an Inspection is immutable — <see cref="AddPhoto"/> and
/// <see cref="UpdateNotes"/> both require <see cref="CompletedAt"/> to still be null. A
/// completed Inspection is the evidentiary basis the Angebot gets built from; a future
/// "reopen" use case, not silent editing, is the intended way to correct one after the fact.
/// </summary>
public sealed class Inspection
{
    private readonly List<InspectionPhoto> _photos = [];

    public int Id { get; private set; }
    public int LeadId { get; private set; }
    public DateTime ScheduledAt { get; private set; }
    public int InspectorId { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public IReadOnlyList<InspectionPhoto> Photos => _photos;

    private Inspection(int leadId, DateTime scheduledAt, int inspectorId)
    {
        LeadId = leadId;
        ScheduledAt = scheduledAt;
        InspectorId = inspectorId;
        Notes = null;
        CompletedAt = null;
    }

    /// <summary>
    /// Schedules a new Inspection. Sequence Diagram §3 Step A. The Application layer is
    /// responsible for calling <c>Lead.MarkInspectionScheduled()</c> after this succeeds
    /// (StateMachine.md §1.3: <c>New → InspectionScheduled</c>) — this aggregate has no
    /// knowledge of Lead as a type and cannot do that itself.
    /// </summary>
    public static Inspection Schedule(int leadId, DateTime scheduledAt, int inspectorId)
    {
        return new Inspection(leadId, scheduledAt, inspectorId);
    }

    /// <summary>
    /// Attaches a photo. Sequence Diagram §3 Step B (repeats per photo). PermissionMatrix.md
    /// §2 restricts who may call this (the assigned Inspector only) — that is an authorization
    /// concern enforced at the API/Application layer, not something this aggregate checks,
    /// consistent with how Lead never checks "who is calling" either. Returns the created
    /// InspectionPhoto — same pattern as <c>AngebotSection.AddItem</c>, since a caller
    /// building a response DTO needs a reference to exactly the child just created, not just
    /// the fact that the collection grew.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection is already completed (BR-10).</exception>
    public InspectionPhoto AddPhoto(string fileUrl, string? caption = null)
    {
        EnsureNotCompleted(nameof(AddPhoto));
        var photo = new InspectionPhoto(fileUrl, caption);
        _photos.Add(photo);
        return photo;
    }

    /// <summary>Sequence Diagram §3 Step B: <c>PATCH /inspections/{id} {notes}</c>.</summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection is already completed (BR-10).</exception>
    public void UpdateNotes(string? notes)
    {
        EnsureNotCompleted(nameof(UpdateNotes));
        Notes = notes?.Trim();
    }

    /// <summary>
    /// Marks the Inspection complete. SRS FR-3.4. The Application layer is responsible for
    /// calling <c>Lead.MarkInspectionDone()</c> after this succeeds (StateMachine.md §1.3:
    /// <c>InspectionScheduled → InspectionDone</c>) — the cross-aggregate check that "this
    /// Inspection belongs to this Lead" happens there, not here, since this aggregate has no
    /// way to look up a Lead itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection was already completed.</exception>
    public void Complete()
    {
        if (CompletedAt is not null)
            throw new InvalidOperationException($"Inspection {Id} is already completed.");

        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reopens a completed Inspection so its record can be corrected, then completed again.
    ///
    /// <para>
    /// <b>This is the action BR-10 itself names, not a relaxation of it.</b> BR-10 states that a
    /// completed Inspection is immutable and that "any future need to change a completed
    /// Inspection's record requires a distinct, explicit action (e.g. a 'reopen' use case), not
    /// implicit editing". The immutability was requested by the Product Owner during Phase 1, so it
    /// is a real requirement and was not weakened here: <see cref="AddPhoto"/>,
    /// <see cref="UpdateNotes"/> and <see cref="Reassign"/> still refuse outright while
    /// <see cref="CompletedAt"/> is set. What changes is that there is now a way to say so
    /// deliberately, instead of the record being unfixable after a typo.
    /// </para>
    /// <para>
    /// <b>The fact that the visit was completed is not erased.</b> Clearing
    /// <see cref="CompletedAt"/> is what makes the aggregate editable again, but the completion and
    /// the reopening are both audited as their own business milestones, so the history reads as
    /// "completed, reopened, completed again" rather than silently as though the first completion
    /// never happened. The AuditLog is the record; this field is the lock.
    /// </para>
    /// <para>
    /// The Lead is deliberately unaffected: it stays at <c>InspectionDone</c>, because the visit did
    /// happen and any Angebot built from it remains valid. Reopening corrects evidence; it does not
    /// rewind the pipeline. The Application layer therefore calls no Lead method here — the mirror
    /// of the note on <see cref="Complete"/>.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection is not currently completed.</exception>
    public void Reopen()
    {
        if (CompletedAt is null)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{nameof(Reopen)}': Inspection {Id} is not completed.");
        }

        CompletedAt = null;
    }

    /// <summary>
    /// Moves this Inspection to a different Inspector. PermissionMatrix.md §2: "Reassign an
    /// Inspection to a different Inspector — Admin only."
    ///
    /// <para>
    /// <b>Guarded by BR-10, unlike <c>Lead.AssignInspector</c> which has no guard at all.</b> The
    /// asymmetry is real, not an inconsistency: a Lead is an open-ended pipeline record that stays
    /// administratively editable for its whole life, whereas a completed Inspection is explicitly
    /// immutable evidence of who was on site and what they found. Rewriting the Inspector on a
    /// finished visit would falsify that record — the same reasoning that already stops notes and
    /// photos changing after completion.
    /// </para>
    /// <para>
    /// The Application layer is responsible for re-applying BR-13 to the Lead afterwards (its
    /// <c>AssignedInspectorId</c> follows the visit), exactly as <see cref="Schedule"/> relies on
    /// it to call <c>Lead.MarkInspectionScheduled()</c> — this aggregate has no knowledge of Lead
    /// as a type. Whether the target is a real, active Inspector is likewise not checkable here;
    /// that needs a query, so it belongs to the handler (CLAUDE.md §2's self-guards-only rule).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection is already completed (BR-10).</exception>
    public void Reassign(int inspectorId)
    {
        EnsureNotCompleted(nameof(Reassign));
        InspectorId = inspectorId;
    }

    private void EnsureNotCompleted(string actionName)
    {
        if (CompletedAt is not null)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{actionName}': Inspection {Id} was completed at {CompletedAt:O} and is now immutable (BR-10).");
        }
    }
}
