using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Application.Leads.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Leads.Commands.CreateLead;

/// <summary>
/// Sequence Diagram §1 (Website) / §2 (Manual). Deliberately thin: validate, create the
/// aggregate, persist, audit, and — only for website-sourced Leads (SRS FR-9.2) — notify the
/// Admin. No branching business logic beyond that single "which channel" dispatch, which is
/// orchestration (which side effect to trigger), not a Domain invariant.
/// </summary>
public sealed class CreateLeadCommandHandler(
    IValidator<CreateLeadCommand> validator,
    ILeadRepository leadRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<CreateLeadCommand, LeadDto>
{
    public async Task<LeadDto> HandleAsync(CreateLeadCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var lead = Lead.Create(command.Name, command.Phone, command.Email, command.Source, command.Address, command.Notes);

        await leadRepository.AddAsync(lead, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.LeadCreated,
            performedByUserId: command.CreatedByUserId,
            details: null,
            cancellationToken);

        var dto = lead.ToDto();

        if (command.Source == LeadSource.Website)
        {
            await emailSender.SendNewWebsiteLeadNotificationAsync(
                new NewWebsiteLeadNotification(lead.Id, lead.Name, lead.Phone, lead.Email),
                cancellationToken);
        }

        return dto;
    }
}
