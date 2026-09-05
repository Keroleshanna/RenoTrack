using FluentValidation;
using RenoTrack.Application.Angebote.Dtos;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Angebote.Commands.ResendAngebot;

/// <summary>
/// SRS FR-6.1a / <b>D99</b>. Re-issues the customer's token link: the previous link is superseded
/// and its replacement created in one commit, so a Lead never holds two working links.
///
/// <para>
/// <b>Both writes commit together</b>, exactly as <c>SendAngebotCommandHandler</c> commits its
/// three. The old link's expiry and the new row share one <c>SaveChangesAsync</c> — both go through
/// the same request-scoped <c>DbContext</c>, so EF Core's implicit transaction covers them. A split
/// would allow a live replacement whose predecessor was never invalidated, which is precisely the
/// invariant this slice exists to hold.
/// </para>
/// <para>
/// <b>No <c>IOwnershipValidator</c>, deliberately.</b> PermissionMatrix §4 marks re-issuing Admin
/// <c>F</c>, not <c>S</c> — any authenticated Admin may re-issue any Angebot, the same reasoning
/// <c>SendAngebotCommandHandler</c> records for sending.
/// </para>
/// <para>
/// <b>No state transition, and <c>SentAt</c> is not touched</b> (D99, Q4). <c>SentAt</c> records
/// the original send; the audit entry is what records each re-issue.
/// </para>
/// </summary>
public sealed class ResendAngebotCommandHandler(
    IValidator<ResendAngebotCommand> validator,
    IAngebotRepository angebotRepository,
    ILeadRepository leadRepository,
    ITokenLinkRepository tokenLinkRepository,
    ITokenLinkService tokenLinkService,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<ResendAngebotCommand, AngebotDto>
{
    public async Task<AngebotDto> HandleAsync(ResendAngebotCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var angebot = await angebotRepository.GetByIdAsync(command.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), command.AngebotId);

        // The one place in this codebase where a handler reads an aggregate's status field, and it
        // is a reasoned exception to CLAUDE.md §6 rather than an oversight (D99, Q1). §6's rule
        // exists to stop a handler duplicating a guard the Domain already enforces — but a re-issue
        // changes no aggregate state, so there is no mutator to call and let throw. The alternative,
        // a public Angebot.EnsureResendable() probe, is exactly the read-only precondition method
        // D29 rejected for Inspection.IsEditable: Domain surface grown to answer a question no
        // mutator asks.
        if (angebot.Status != AngebotStatus.Sent)
        {
            throw new ConflictException(
                $"Angebot {angebot.AngebotNumber} is {angebot.Status} and its link cannot be " +
                "re-issued — only an Angebot awaiting the customer's decision has a link to replace.");
        }

        var lead = await leadRepository.GetByIdAsync(angebot.LeadId, cancellationToken)
            ?? throw new NotFoundException(nameof(Lead), angebot.LeadId);

        var current = await tokenLinkRepository.FindCurrentForAngebotAsync(angebot.Id, cancellationToken)
            ?? throw new ConflictException(
                $"Angebot {angebot.AngebotNumber} has no token link to re-issue.");

        // Supersede first, create second — the ordering the invariant depends on, and the ordering
        // that makes the concurrency predicate exist. Expire() always writes, even for a link that
        // has already lapsed naturally (D99, Q2): that UPDATE is what carries
        // WHERE ExpiresAt = @original, and it is what lets exactly one of two concurrent re-issues
        // win. Its own guard refuses a link that already carried a decision (BR-4).
        current.Expire();

        var generated = tokenLinkService.Generate();
        var replacement = TokenLink.Create(
            TokenLinkEntityType.Angebot, angebot.Id, generated.Token, generated.ExpiresAt);
        await tokenLinkRepository.AddAsync(replacement, cancellationToken);

        // One batch, one transaction. If a concurrent re-issue committed first, this UPDATE matches
        // zero rows, EF throws, UnitOfWork translates it to ConflictException (D96) and the whole
        // batch rolls back — taking the replacement with it, so the loser leaves no trace. If a
        // customer's decision committed first, the same UPDATE misses on UsedAt instead, with the
        // same outcome. Neither race needs anything beyond this call.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Against Angebot, not Lead — the opposite of AngebotSent, because no Lead-level milestone
        // occurred (see AuditAction.AngebotLinkReissued). After the commit, never before:
        // AuditService shares this request's DbContext.
        await auditService.LogAsync(
            entityType: nameof(Angebot),
            entityId: angebot.Id,
            action: AuditAction.AngebotLinkReissued,
            performedByUserId: command.ResentByAdminId,
            details: $"The customer's link for Angebot {angebot.AngebotNumber} was re-issued; the previous link was superseded.",
            cancellationToken);

        // The same notification the original send used — Q3 required no second email mechanism, and
        // there is none. It carries only the new token; the superseded one is never re-sent.
        await emailSender.SendAngebotReadyNotificationAsync(
            new AngebotReadyNotification(angebot.Id, angebot.AngebotNumber, lead.Name, lead.Email, generated.Token),
            cancellationToken);

        return angebot.ToDto();
    }
}
