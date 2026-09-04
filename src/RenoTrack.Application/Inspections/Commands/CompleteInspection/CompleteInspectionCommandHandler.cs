using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Inspections.Commands.CompleteInspection;

/// <summary>
/// Sequence Diagram §3 Step B (end). StateMachine.md §1.3's "Inspection belongs to this Lead"
/// guard is satisfied structurally by this command's shape: the Lead loaded here is always the
/// one referenced by <c>inspection.LeadId</c>, so there is no separate check to perform.
///
/// The ownership check below ("is this the assigned Inspector?") is a business invariant of
/// this use case, not an authorization attribute (Architecture.md §7.3) — it requires the
/// loaded Inspection to evaluate, so it lives here rather than at the API layer.
///
/// Audit entry is logged against the Lead, not the Inspection — same principle as
/// ScheduleInspectionCommandHandler (Architecture.md §11): the business-meaningful transition
/// is the Lead reaching InspectionDone.
/// </summary>
public sealed class CompleteInspectionCommandHandler(
    IValidator<CompleteInspectionCommand> validator,
    IInspectionRepository inspectionRepository,
    ILeadRepository leadRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IOwnershipValidator ownershipValidator) : ICommandHandler<CompleteInspectionCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(CompleteInspectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        ownershipValidator.EnsureInspectionOwnership(inspection, command.CompletedByInspectorId);

        var lead = await leadRepository.GetByIdAsync(inspection.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), inspection.LeadId);

        inspection.Complete();

        // Only on the *first* completion. A visit reopened under BR-10 (Inspection.Reopen) has
        // already driven the Lead to InspectionDone, and MarkInspectionDone only runs from
        // InspectionScheduled -- so re-completing a corrected visit threw and surfaced as a 409,
        // leaving the visit permanently open. Found by driving reopen in the browser; the Domain
        // test passed because it exercises the aggregate alone, never this cross-aggregate step.
        //
        // This is CLAUDE.md §6's sanctioned "an if that decides which side effect to trigger",
        // not a duplicated guard: Inspection.Complete() above still runs unconditionally and still
        // refuses a second completion on its own. What is conditional is the *Lead* transition,
        // which is a pipeline milestone that happens once.
        if (!LeadHasAlreadyPassedTheVisit(lead.Status))
        {
            lead.MarkInspectionDone();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.InspectionDone,
            performedByUserId: command.CompletedByInspectorId,
            details: null,
            cancellationToken);

        return inspection.ToDto();
    }
    /// <summary>
    /// Whether this Lead has already moved past the site visit in the pipeline.
    ///
    /// <para>
    /// Deliberately an explicit set rather than an ordinal comparison: <c>Lost</c> sits last in the
    /// enum but is a terminal branch, not a later stage, so "greater than InspectionScheduled" would
    /// be true for the wrong reason. Listing the states says what is meant.
    /// </para>
    /// <para>
    /// A Lead that has <em>not</em> reached the visit is deliberately absent from this set, so
    /// <c>MarkInspectionDone()</c> still runs and still throws for it -- that combination means the
    /// Inspection and its Lead genuinely disagree, which BR-13 makes unreachable in production and
    /// which should fail loudly rather than be skipped.
    /// </para>
    /// </summary>
    private static bool LeadHasAlreadyPassedTheVisit(LeadStatus status) => status is
        LeadStatus.InspectionDone
        or LeadStatus.AngebotInProgress
        or LeadStatus.AngebotSent
        or LeadStatus.Won
        or LeadStatus.Lost;

}
