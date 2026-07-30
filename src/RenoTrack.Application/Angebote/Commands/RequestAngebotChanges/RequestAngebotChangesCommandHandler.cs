using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;

/// <summary>
/// Sequence Diagram §5 (Admin requests changes) / StateMachine §2.3. No IOwnershipValidator
/// call — same role-based reasoning as ApproveAngebotCommand (PermissionMatrix §4: Admin-"F").
///
/// Angebot.RequestChanges(reviewedByAdminId) only performs the workflow transition (self-guard
/// Status == InReview) and records ReviewedByAdminId — it knows nothing about comment
/// persistence. The AngebotReviewComment is created independently here in the Application
/// layer, because it belongs to its own aggregate (Architecture §6), not to Angebot — the same
/// separation reasoning that kept it out of the Angebot aggregate in the first place.
/// </summary>
public sealed class RequestAngebotChangesCommandHandler(
    IValidator<RequestAngebotChangesCommand> validator,
    IAngebotRepository angebotRepository,
    IAngebotReviewCommentRepository reviewCommentRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<RequestAngebotChangesCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(RequestAngebotChangesCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        angebot.RequestChanges(command.ReviewedByAdminId); // Domain self-guard (Status == InReview)

        var reviewComment = AngebotReviewComment.Create(angebot.Id, command.ReviewedByAdminId, command.Comment);
        await reviewCommentRepository.AddAsync(reviewComment, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotChangesRequested,
            performedByUserId: command.ReviewedByAdminId,
            details: null,
            cancellationToken);

        await emailSender.SendAngebotChangesRequestedNotificationAsync(
            new AngebotChangesRequestedNotification(angebot.Id, angebot.AngebotNumber, command.Comment, angebot.CreatedByInspectorId),
            cancellationToken);

        return angebot.ToDto();
    }
}
