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
    /// consistent with how Lead never checks "who is calling" either.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if this Inspection is already completed (BR-10).</exception>
    public void AddPhoto(string fileUrl, string? caption = null)
    {
        EnsureNotCompleted(nameof(AddPhoto));
        _photos.Add(new InspectionPhoto(fileUrl, caption));
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

    private void EnsureNotCompleted(string actionName)
    {
        if (CompletedAt is not null)
        {
            throw new InvalidOperationException(
                $"Cannot perform '{actionName}': Inspection {Id} was completed at {CompletedAt:O} and is now immutable (BR-10).");
        }
    }
}
