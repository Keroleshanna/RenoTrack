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
/// milestone rather than an editing operation — both guards (Status is Draft or ChangesRequested,
/// at least one section with at least one item) live entirely inside Angebot.SubmitForReview(),
/// not duplicated here. Audited against the Angebot itself, not the Lead: unlike
/// CreateAngebotCommand, this transition never touches Lead.Status (StateMachine §1.3:
/// "AngebotInProgress -> (internal Angebot events) -> AngebotInProgress, no Lead-level
/// change"). Notification only sent after persistence succeeds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owning Inspector or any Admin</b> (D115). The audit row names whoever submitted.
/// </para>
/// <para>
/// <b>The review email goes out only when an Inspector submits.</b> SRS FR-9.2 names the trigger as
/// "an Inspector submits an Angebot for review": its purpose is to tell the Admins a document has
/// arrived for them. An Admin who submits — typically after correcting the Angebot themselves — is
/// one of the people that email exists to inform, and no document names a notification for it.
/// That <c>if</c> chooses whether a side effect fires, which CLAUDE.md §6 allows; it decides
/// nothing about the transition.
/// </para>
/// </remarks>
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

        if (command.RequestingInspectorId is { } inspectorId)
        {
            ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId);
        }

        AngebotFreshness.EnsureExpectedVersion(angebot, command.ExpectedVersion);

        angebot.SubmitForReview(); // Domain self-guards: Draft/ChangesRequested, >= 1 section with >= 1 item

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotSubmittedForReview,
            performedByUserId: command.PerformedByUserId,
            details: null,
            cancellationToken);

        if (command.RequestingInspectorId is not null)
        {
            await emailSender.SendAngebotSubmittedForReviewNotificationAsync(
                new AngebotSubmittedForReviewNotification(angebot.Id, angebot.AngebotNumber, angebot.LeadId),
                cancellationToken);
        }

        return angebot.ToDto();
    }
}
