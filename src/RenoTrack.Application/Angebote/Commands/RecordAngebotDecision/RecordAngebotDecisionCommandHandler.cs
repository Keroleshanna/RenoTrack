using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Commands.RecordAngebotDecision;

/// <summary>
/// SRS FR-6.3/FR-6.5 / Sequence Diagram §6 and §12 / StateMachine.md §2.3 and §5. The customer's
/// answer, and the only place in the system where a Lead may reach <c>Won</c> or <c>Lost</c>.
///
/// <para>
/// <b>Three aggregates, one commit.</b> <c>TokenLink.MarkUsed()</c>, the Angebot's decision and the
/// Lead's transition share a single <c>SaveChangesAsync</c> — both repositories and
/// <c>IUnitOfWork</c> resolve the same request-scoped <c>DbContext</c>, so EF Core's implicit
/// transaction covers all three. StateMachine.md §5 states this as an invariant rather than a
/// preference: "Lead.Status is only set to <c>Won</c> inside the same transaction as the Angebot
/// decision handler". Any split would allow a customer-approved Angebot whose Lead never moved, or
/// a consumed token whose decision was never recorded — the second of which would lock the customer
/// out of answering at all.
/// </para>
/// <para>
/// <b>Every state rule is left to the aggregate that owns it.</b> There is no handler-level check
/// that the token is unused, that the Angebot is <c>Sent</c>, or that the Lead is
/// <c>AngebotSent</c>: <c>MarkUsed()</c>, <c>RecordCustomerApproval()</c>/<c>RecordCustomerRejection()</c>
/// and <c>MarkWon()</c>/<c>MarkLost()</c> each guard their own precondition and throw, which the API
/// maps to 409. Re-checking them here to avoid a Domain exception is exactly what CLAUDE.md §6
/// forbids. <b>Reusing an already-decided link is therefore a 409, not a 410</b> — the link still
/// exists and stays readable through the GET endpoint (BR-4), so it is not "gone"; what conflicts
/// is the decision already recorded against it (CLAUDE.md §17's definition of `ConflictException`).
/// </para>
/// <para>
/// <b>Expiry is the one check performed here</b>, ahead of <c>MarkUsed()</c>'s own equivalent guard,
/// because Sequence Diagram §6 names 410 for that case specifically and §12 requires the reason to
/// be distinguishable. That is producing a documented status, not duplicating an invariant — the
/// aggregate still refuses regardless, as its own tests prove.
/// </para>
/// <para>
/// <b>No ownership check, and no user id anywhere.</b> Possession of the token is the entire
/// authorisation model (Architecture.md §7.2), so the audit entry's <c>performedByUserId</c> is
/// null — ERD.md's own meaning for that column ("nullable = system-triggered action"), and the only
/// honest value when the actor is a customer with no account.
/// </para>
/// </summary>
public sealed class RecordAngebotDecisionCommandHandler(
    IValidator<RecordAngebotDecisionCommand> validator,
    ITokenLinkRepository tokenLinkRepository,
    IAngebotRepository angebotRepository,
    ILeadRepository leadRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<RecordAngebotDecisionCommand, PublicAngebotDto>
{
    public async Task<PublicAngebotDto> HandleAsync(
        RecordAngebotDecisionCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var tokenLink = await tokenLinkRepository.FindByTokenAsync(command.Token, cancellationToken);

        // One combined condition, so an Invoice token and an unknown token cannot drift into
        // producing distinguishable responses — the same shape as the read query.
        if (tokenLink is null || tokenLink.EntityType != TokenLinkEntityType.Angebot)
        {
            throw new NotFoundException("This link is not valid.");
        }

        if (tokenLink.IsExpired(DateTime.UtcNow))
        {
            throw new GoneException("This link has expired and can no longer be used.");
        }

        var angebot = await angebotRepository.GetByIdAsync(tokenLink.EntityId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), tokenLink.EntityId);

        var lead = await leadRepository.GetByIdAsync(angebot.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), angebot.LeadId);

        // BR-4 first: consuming the link is what makes this decision final, and if it has already
        // been consumed nothing else should be touched.
        tokenLink.MarkUsed();

        var approved = command.Decision == CustomerDecision.Approve;
        if (approved)
        {
            angebot.RecordCustomerApproval();
            lead.MarkWon();
        }
        else
        {
            angebot.RecordCustomerRejection();
            lead.MarkLost();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit, never before (D50's stated precondition, and AuditService shares this
        // request's DbContext). Logged against Lead for the reason AuditAction documents.
        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: approved ? AuditAction.AngebotCustomerApproved : AuditAction.AngebotCustomerRejected,
            performedByUserId: null,
            details: $"Angebot {angebot.AngebotNumber} was {(approved ? "approved" : "rejected")} by the customer.",
            cancellationToken);

        await emailSender.SendAngebotDecisionNotificationAsync(
            new AngebotDecisionNotification(angebot.Id, angebot.AngebotNumber, lead.Id, lead.Name, approved),
            cancellationToken);

        return angebot.ToPublicDto();
    }
}
