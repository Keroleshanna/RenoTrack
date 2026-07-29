using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.SubmitAngebotForReview;

/// <summary>
/// Sequence Diagram §5 / StateMachine §2.3. The first Angebot command that is a true business
/// milestone rather than an editing operation — both guards (Status == Draft, at least one
/// section with at least one item) live entirely inside Angebot.SubmitForReview(), not
/// duplicated here. Audited against the Angebot itself, not the Lead: unlike
/// CreateAngebotCommand, this transition never touches Lead.Status (StateMachine §1.3:
/// "AngebotInProgress -> (internal Angebot events) -> AngebotInProgress, no Lead-level
/// change"). Notification only sent after persistence succeeds.
/// </summary>
public sealed class SubmitAngebotForReviewCommandHandler(
    IValidator<SubmitAngebotForReviewCommand> validator,
    IAngebotRepository angebotRepository,
    IOwnershipValidator ownershipValidator,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<SubmitAngebotForReviewCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(SubmitAngebotForReviewCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        ownershipValidator.EnsureAngebotOwnership(angebot, command.InspectorId);

        angebot.SubmitForReview(); // Domain self-guards: Status == Draft, >= 1 section with >= 1 item

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotSubmittedForReview,
            performedByUserId: command.InspectorId,
            details: null,
            cancellationToken);

        await emailSender.SendAngebotSubmittedForReviewNotificationAsync(
            new AngebotSubmittedForReviewNotification(angebot.Id, angebot.AngebotNumber, angebot.LeadId),
            cancellationToken);

        return angebot.ToDto();
    }
}
