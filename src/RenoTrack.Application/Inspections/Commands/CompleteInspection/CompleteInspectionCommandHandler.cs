using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

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
    IAuditService auditService) : ICommandHandler<CompleteInspectionCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(CompleteInspectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        if (inspection.InspectorId != command.CompletedByInspectorId)
        {
            throw new ForbiddenException(
                $"Inspector {command.CompletedByInspectorId} is not assigned to Inspection {inspection.Id}.");
        }

        var lead = await leadRepository.GetByIdAsync(inspection.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), inspection.LeadId);

        inspection.Complete();
        lead.MarkInspectionDone();

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
}
