using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote.Commands.ReturnAngebotForCorrection;

/// <summary>
/// StateMachine.md §2.3 <c>InReview → Draft</c> (<b>D115</b>). The <c>InReview</c> guard is the
/// aggregate's own (<c>Angebot.ReturnForCorrection</c>) and is not duplicated here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate event rather than Request Changes.</b> The Product Owner's requirement was that
/// a small fix spotted in review must not need a round trip through Request Changes, which demands a
/// comment and emails the Inspector. This event does neither. The Angebot returns to <c>Draft</c>, the
/// Admin corrects it under the ordinary edit lock, and it must then be submitted and approved again —
/// so the review gate (BR-1) still stands between the correction and the customer.
/// </para>
/// <para>
/// <b>No ownership check</b> — an Admin-<c>F</c> action (CLAUDE.md §16) — and <b>no notification</b>,
/// because no document names one (CLAUDE.md §11). Audited against the Angebot, like the other
/// internal review actions, which never move the Lead.
/// </para>
/// </remarks>
public sealed class ReturnAngebotForCorrectionCommandHandler(
    IValidator<ReturnAngebotForCorrectionCommand> validator,
    IAngebotRepository angebotRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<ReturnAngebotForCorrectionCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(ReturnAngebotForCorrectionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        AngebotFreshness.EnsureExpectedVersion(angebot, command.ExpectedVersion);

        angebot.ReturnForCorrection(); // Domain self-guard: Status == InReview

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotReturnedForCorrection,
            performedByUserId: command.PerformedByAdminId,
            details: null,
            cancellationToken);

        return angebot.ToDto();
    }
}
