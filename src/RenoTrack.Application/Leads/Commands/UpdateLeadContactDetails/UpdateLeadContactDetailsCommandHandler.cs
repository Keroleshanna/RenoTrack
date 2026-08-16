using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Leads.Commands.UpdateLeadContactDetails;

/// <summary>
/// Corrects a Lead's contact details (<c>PermissionMatrix.md</c> §1). The canonical handler shape
/// from CLAUDE.md §6: validate, load, enforce ownership, invoke one Domain method, persist, audit,
/// map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership is conditional because the permission is split.</b> §1 marks this row Admin
/// <c>F</c> / Inspector <c>S</c>, so the validator is called for an Inspector and deliberately not
/// for an Admin — invoking it for an <c>F</c> caller would be a semantic error rather than merely
/// redundant work (CLAUDE.md §16). This mirrors <c>GetLeadByIdQueryHandler</c>, which already
/// resolves the identical split for the read of the same aggregate.
/// </para>
/// <para>
/// No notification: SRS FR-9.2 enumerates three Admin-facing triggers and a contact correction is
/// not among them, so sending one would be speculative (CLAUDE.md §11).
/// </para>
/// </remarks>
public sealed class UpdateLeadContactDetailsCommandHandler(
    IValidator<UpdateLeadContactDetailsCommand> validator,
    ILeadRepository leadRepository,
    IUnitOfWork unitOfWork,
    IOwnershipValidator ownershipValidator,
    IAuditService auditService) : ICommandHandler<UpdateLeadContactDetailsCommand, LeadDto>
{
    public async Task<LeadDto> HandleAsync(
        UpdateLeadContactDetailsCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), command.LeadId);

        if (command.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureLeadOwnership(lead, inspectorId);
        }

        lead.UpdateContactDetails(command.Name, command.Phone, command.Email, command.Address);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.LeadContactDetailsUpdated,
            // The caller's identity, never the scoping value — that one is null for an Admin, which
            // would leave every Admin-made correction attributed to nobody.
            performedByUserId: command.PerformedByUserId,
            details: null,
            cancellationToken);

        return lead.ToDto();
    }
}
