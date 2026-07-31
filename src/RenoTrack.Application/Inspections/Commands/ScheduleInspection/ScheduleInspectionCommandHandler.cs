using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Commands.ScheduleInspection;

/// <summary>
/// Sequence Diagram §3 Step A. Also carries out BR-13: the scheduled Inspector becomes the
/// Lead's AssignedInspectorId in the same operation. Lead.MarkInspectionScheduled()'s own
/// self-guard (Status must be New) covers StateMachine.md §1.3's guard for this transition —
/// the handler does not re-check status itself.
///
/// The audit entry is logged against the Lead, not the newly-created Inspection — see
/// Architecture.md §11: the business-meaningful transition is the Lead's, and AuditLog has no
/// linkage that would surface an Inspection-typed entry on the Lead's own activity timeline
/// (Wireframe C1).
/// </summary>
public sealed class ScheduleInspectionCommandHandler(
    IValidator<ScheduleInspectionCommand> validator,
    ILeadRepository leadRepository,
    IInspectionRepository inspectionRepository,
    IUserQueries userQueries,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ScheduleInspectionCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(ScheduleInspectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), command.LeadId);

        // An Inspection assigned to a non-existent user, a deactivated account, or someone who is
        // not an Inspector is invalid business data, so it is rejected here rather than left to the
        // database FK constraint — which would only catch the first of those three, and would
        // surface as an unmapped DbUpdateException (a 500) on an ordinary client mistake.
        //
        // NotFoundException, not Conflict: from the caller's point of view the resource they named —
        // an assignable Inspector with that id — does not exist. That framing is honest for all
        // three cases and, as a side benefit, does not disclose whether the id belongs to some other
        // kind of account.
        if (!await userQueries.IsActiveInspectorAsync(command.InspectorId, cancellationToken))
        {
            throw new NotFoundException("Inspector", command.InspectorId);
        }

        var inspection = Inspection.Schedule(command.LeadId, command.ScheduledAt, command.InspectorId);

        lead.AssignInspector(command.InspectorId); // BR-13
        lead.MarkInspectionScheduled();

        await inspectionRepository.AddAsync(inspection, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.InspectionScheduled,
            performedByUserId: command.ScheduledByAdminId,
            details: null,
            cancellationToken);

        return inspection.ToDto();
    }
}
