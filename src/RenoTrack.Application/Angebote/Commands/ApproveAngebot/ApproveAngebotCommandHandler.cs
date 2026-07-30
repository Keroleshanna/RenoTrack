using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.ApproveAngebot;

/// <summary>
/// Sequence Diagram §5 (Admin approves) / StateMachine §2.3. Deliberately has no
/// IOwnershipValidator call: PermissionMatrix §4 marks this Admin-"F" (full access), not "S"
/// (owner-scoped) — "any authenticated Admin may approve any Angebot" is a role-based rule,
/// resolved entirely at the API layer (Architecture §7.3), not an ownership business
/// invariant. Angebot.Approve(reviewedByAdminId) self-guards Status == InReview and records
/// ReviewedByAdminId itself. No notification — internal approval isn't customer-facing; that
/// happens in the later Send Angebot workflow.
/// </summary>
public sealed class ApproveAngebotCommandHandler(
    IValidator<ApproveAngebotCommand> validator,
    IAngebotRepository angebotRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ApproveAngebotCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(ApproveAngebotCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        angebot.Approve(command.ReviewedByAdminId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotApproved,
            performedByUserId: command.ReviewedByAdminId,
            details: null,
            cancellationToken);

        return angebot.ToDto();
    }
}
