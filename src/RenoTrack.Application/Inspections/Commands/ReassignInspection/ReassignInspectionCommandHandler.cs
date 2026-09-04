using System.Globalization;
using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Inspections.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Commands.ReassignInspection;

/// <summary>
/// Reassigns a scheduled Inspection (<c>PermissionMatrix.md</c> §2). Admin <c>F</c>, so no
/// <c>IOwnershipValidator</c> call belongs here (CLAUDE.md §16) — the outgoing Inspector's
/// ownership is precisely what is being taken away, so consulting it would be backwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Re-applies BR-13, mirroring <c>ScheduleInspectionCommandHandler</c>.</b> That rule states
/// scheduling an Inspection assigns its Inspector to the Lead; the same must hold when the visit
/// moves, or the Lead would keep pointing at a colleague who is no longer going. Leaving them
/// assigned would also keep the Lead in the wrong Inspector's server-side-filtered pipeline
/// (§1) while the new one could not see it — a scoping bug, not just stale data.
/// </para>
/// <para>
/// Both mutations are committed by one <c>SaveChangesAsync</c>, so the Inspection and its Lead can
/// never disagree about who is responsible.
/// </para>
/// <para>
/// BR-10 is enforced by <c>Inspection.Reassign</c> itself and deliberately not re-checked here —
/// the handler calls the Domain method and lets it throw (CLAUDE.md §6), surfacing as 409 through
/// the <c>InvalidOperationException</c> arm of the ProblemDetails switch.
/// </para>
/// </remarks>
public sealed class ReassignInspectionCommandHandler(
    IValidator<ReassignInspectionCommand> validator,
    IInspectionRepository inspectionRepository,
    ILeadRepository leadRepository,
    IUserQueries userQueries,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ReassignInspectionCommand, InspectionDto>
{
    public async Task<InspectionDto> HandleAsync(
        ReassignInspectionCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var inspection = await inspectionRepository.GetByIdAsync(command.InspectionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Inspection), command.InspectionId);

        // Same framing as ScheduleInspectionCommandHandler: from the caller's side, an assignable
        // Inspector with that id does not exist — true for a missing, deactivated or wrong-role
        // account alike, none of which a database FK could distinguish (D62).
        if (!await userQueries.IsActiveInspectorAsync(command.InspectorId, cancellationToken))
        {
            throw new NotFoundException("Inspector", command.InspectorId);
        }

        var lead = await leadRepository.GetByIdAsync(inspection.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), inspection.LeadId);

        inspection.Reassign(command.InspectorId); // BR-10 self-guard fires here
        lead.AssignInspector(command.InspectorId); // BR-13

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Against the Lead, exactly like InspectionScheduled — Architecture.md §11: AuditLog has no
        // linkage that would surface an Inspection-typed row on the Lead's own activity timeline
        // (Wireframe C1), and the Lead's assigned Inspector is what visibly changed.
        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.InspectionReassigned,
            performedByUserId: command.ReassignedByAdminId,
            details: command.InspectorId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return inspection.ToDto();
    }
}
