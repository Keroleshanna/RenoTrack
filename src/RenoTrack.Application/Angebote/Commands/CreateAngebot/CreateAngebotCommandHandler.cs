using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.CreateAngebot;

/// <summary>
/// Sequence Diagram §4 "Create the draft". StateMachine.md §2.4's "Lead.Status ==
/// InspectionDone" guard is covered by Lead.MarkAngebotInProgress()'s own self-guard — not
/// re-checked here. The "only one active Angebot per Lead" half of §2.4 genuinely needs a
/// repository query (Angebot has no visibility into its siblings), so it lives here.
///
/// Audit entry logged against the Lead (Architecture.md §11): creating a draft drives
/// Lead.MarkAngebotInProgress(), a Lead-level business milestone, even though Sequence
/// Diagram §4 originally omitted this step (since corrected there too).
/// </summary>
public sealed class CreateAngebotCommandHandler(
    IValidator<CreateAngebotCommand> validator,
    ILeadRepository leadRepository,
    IAngebotRepository angebotRepository,
    INumberGeneratorService numberGenerator,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<CreateAngebotCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(CreateAngebotCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var lead = await leadRepository.GetByIdAsync(command.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), command.LeadId);

        ownershipValidator.EnsureLeadOwnership(lead, command.CreatedByInspectorId);

        if (await angebotRepository.HasActiveAngebotForLeadAsync(command.LeadId, cancellationToken))
        {
            throw new ConflictException($"Lead {command.LeadId} already has an active Angebot.");
        }

        var angebotNumber = await numberGenerator.NextAngebotNumberAsync(DateTime.UtcNow.Year, cancellationToken);

        var angebot = Angebot.Create(command.LeadId, command.InspectionId, angebotNumber, command.CreatedByInspectorId);

        lead.MarkAngebotInProgress(); // Domain self-guard (Status == InspectionDone)

        await angebotRepository.AddAsync(angebot, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.AngebotCreated,
            performedByUserId: command.CreatedByInspectorId,
            details: null,
            cancellationToken);

        return angebot.ToDto();
    }
}
