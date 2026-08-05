using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Commands.SendAngebot;

/// <summary>
/// SRS FR-6.1 / Sequence Diagram §6. The moment an internally approved Angebot becomes a customer-
/// facing document: the Angebot moves <c>ApprovedInternally → Sent</c>, the Lead moves
/// <c>AngebotInProgress → AngebotSent</c>, and a single-use token link is issued.
///
/// No IOwnershipValidator call, deliberately: PermissionMatrix.md §4 marks "Send Angebot to Lead
/// (generate token link)" as Admin-"F" (full access), not "S" — any authenticated Admin may send
/// any Angebot. Using ownership here would be the semantic error CLAUDE.md §16 describes, the same
/// reasoning D31 recorded for Approve/RequestChanges.
///
/// **All three writes commit together.** Angebot.Status, Lead.Status and the TokenLink row share
/// one SaveChangesAsync — both repositories and IUnitOfWork resolve the same request-scoped
/// DbContext, so EF Core's implicit transaction covers all three. This matters more here than
/// almost anywhere else in the system: a committed token link whose Angebot never reached
/// <c>Sent</c> would be a live customer-facing credential for a document nobody believes was sent,
/// and a <c>Sent</c> Angebot with no token link is a customer who can never respond.
///
/// **StateMachine.md §2.3's "Lead has a valid email address" guard is not re-checked here, and that
/// is a considered conclusion rather than an oversight** — see the Slice 2 record in
/// PHASE6_PROGRESS.md. Presence is already structurally guaranteed (Lead.Create rejects a blank
/// email, Lead exposes no way to change it afterwards, and the column is NOT NULL), so a presence
/// check would be unreachable code. Format is enforced by CreateLeadCommandValidator's
/// <c>.EmailAddress()</c>, and re-running shape validation inside a handler is what CLAUDE.md §5
/// and §6 both forbid. The residual risk is recorded rather than papered over: that format
/// guarantee rests on one validator at what is currently the only Lead.Create call site.
/// </summary>
public sealed class SendAngebotCommandHandler(
    IValidator<SendAngebotCommand> validator,
    IAngebotRepository angebotRepository,
    ILeadRepository leadRepository,
    ITokenLinkRepository tokenLinkRepository,
    ITokenLinkService tokenLinkService,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<SendAngebotCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(SendAngebotCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        var lead = await leadRepository.GetByIdAsync(angebot.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), angebot.LeadId);

        // Both Domain guards run before a token is generated, so a rejected send leaves no trace:
        // Angebot.Send() self-guards Status == ApprovedInternally, Lead.MarkAngebotSent() guards
        // Status == AngebotInProgress. Nothing here is irreversible (the token is in-memory until
        // SaveChangesAsync), but the §12 ordering principle applies anyway — guards first.
        angebot.Send();
        lead.MarkAngebotSent();

        var generated = tokenLinkService.Generate();
        var tokenLink = TokenLink.Create(TokenLinkEntityType.Angebot, angebot.Id, generated.Token, generated.ExpiresAt);
        await tokenLinkRepository.AddAsync(tokenLink, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Logged against Lead, not Angebot: this command's business-meaningful transition is the
        // Lead reaching AngebotSent (CLAUDE.md §10), the same reasoning that puts AngebotCreated
        // on Lead. Sequencing matters too — AuditService shares this request's DbContext, so it
        // must run after the business commit, never before.
        await auditService.LogAsync(
            entityType: nameof(Lead),
            entityId: lead.Id,
            action: AuditAction.AngebotSent,
            performedByUserId: command.SentByAdminId,
            details: $"Angebot {angebot.AngebotNumber} sent to the customer.",
            cancellationToken);

        // After the commit, never before (CLAUDE.md §11) — this email hands the customer a working
        // credential, so it must never describe a state that failed to persist.
        await emailSender.SendAngebotReadyNotificationAsync(
            new AngebotReadyNotification(angebot.Id, angebot.AngebotNumber, lead.Name, lead.Email, generated.Token),
            cancellationToken);

        return angebot.ToDto();
    }
}
