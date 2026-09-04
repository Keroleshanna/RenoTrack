using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Commands.ReopenInspection;

/// <summary>
/// Reopens a completed Inspection (BR-10). Scoped to the assigned Inspector, like every other edit
/// on an Inspection (`PermissionMatrix.md` §2 marks photos, notes and completion Inspector `S` /
/// Admin `—`, so the action that *enables* those edits belongs to the same person).
/// </summary>
/// <remarks>
/// <para>
/// <b>Audited, unlike the edits it enables.</b> Uploading a photo and revising notes are
/// operational activity (CLAUDE.md §10), but reopening is a deliberate reversal of a workflow gate
/// — the one fact someone reviewing this Inspection's history would need in order to read the
/// evidence correctly. Logged against the <c>Lead</c>, following <c>InspectionDone</c>, for
/// Architecture.md §11's reason: an Inspection-typed row would never surface on the Lead's own
/// activity timeline.
/// </para>
/// <para>
/// <b>The Lead is deliberately not moved back.</b> It stays at <c>InspectionDone</c> — the visit
/// did happen, and any Angebot already built from it stays valid. Reopening corrects the evidence;
/// it does not rewind the pipeline. Calling <c>Lead.MarkInspectionScheduled()</c> here would also
/// be refused outright, since that transition only runs from <c>New</c>.
/// </para>
/// </remarks>
public sealed class ReopenInspectionCommandHandler(
    IValidator<ReopenInspectionCommand> validator,
    IInspectionRepository inspectionRepository,
    IUnitOfWork unitOfWork,
    IOwnershipValidator ownershipValidator,
    IAuditService auditService) : ICommandHandler<ReopenInspectionCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(
        ReopenInspectionCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        ownershipValidator.EnsureInspectionOwnership(inspection, command.ReopenedByInspectorId);

        // Refuses with 409 if it was never completed — the handler does not pre-check (CLAUDE.md §6).
        inspection.Reopen();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: inspection.LeadId,
            action: AuditAction.InspectionReopened,
            performedByUserId: command.ReopenedByInspectorId,
            details: null,
            cancellationToken);

        return inspection.ToDto();
    }
}
