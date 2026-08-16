using System.Globalization;
using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Leads.Commands.AssignLeadInspector;

/// <summary>
/// Assigns or reassigns a Lead's Inspector (<c>PermissionMatrix.md</c> §1). The canonical shape
/// from CLAUDE.md §6, with no ownership step: §1 marks this row Admin <c>F</c>, so it is
/// role-based only and an <c>IOwnershipValidator</c> call here would be a semantic error, not
/// merely redundant (CLAUDE.md §16).
/// </summary>
/// <remarks>
/// <para>
/// The eligibility check reuses <c>IUserQueries.IsActiveInspectorAsync</c> for D62's reason: a
/// database FK catches only non-existence, surfaces as an unmapped 500 on an ordinary mistyped id,
/// and cannot catch "real user, wrong role" or "right role, deactivated account" at all. The
/// <c>NotFoundException</c> framing matches <c>ScheduleInspectionCommandHandler</c> exactly — from
/// the caller's side, the resource they named (an assignable Inspector with that id) does not
/// exist, which is honest for all three cases and discloses nothing about other account types.
/// </para>
/// <para>
/// <b>Reassigning to the Inspector already assigned is allowed and is not treated as a conflict.</b>
/// No document forbids it, the result is the state the caller asked for, and refusing it would
/// invent a rule. It still writes an audit row, which is the honest record of an Admin having
/// confirmed the assignment.
/// </para>
/// </remarks>
public sealed class AssignLeadInspectorCommandHandler(
    IValidator<AssignLeadInspectorCommand> validator,
    ILeadRepository leadRepository,
    IUserQueries userQueries,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<AssignLeadInspectorCommand, LeadDto>
{
    public async Task<LeadDto> HandleAsync(
        AssignLeadInspectorCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), command.LeadId);

        if (!await userQueries.IsActiveInspectorAsync(command.InspectorId, cancellationToken))
        {
            throw new NotFoundException("Inspector", command.InspectorId);
        }

        lead.AssignInspector(command.InspectorId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.LeadInspectorAssigned,
            performedByUserId: command.AssignedByAdminId,
            // Who it went to. Without this the trail records that an assignment happened but not to
            // whom, which is the only fact that makes the entry worth reading.
            details: command.InspectorId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return lead.ToDto();
    }
}
