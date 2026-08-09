using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Common.Notifications;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Commands.SendInvoice;

/// <summary>
/// SRS FR-8.3 / Sequence Diagram §9, first half. The moment an Invoice becomes a customer-facing
/// document: the Invoice moves <c>Draft → Sent</c> and a token link is issued.
///
/// <para>
/// <b>No <c>IOwnershipValidator</c>, deliberately.</b> `PermissionMatrix.md` §5 marks "Send Invoice"
/// Admin <c>F</c> — the same reasoning that keeps one out of <c>SendAngebotCommandHandler</c>
/// (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>Both writes commit together.</b> <c>Invoice.Status</c> and the <c>TokenLink</c> row share one
/// <c>SaveChangesAsync</c> — both repositories and <c>IUnitOfWork</c> resolve the same
/// request-scoped <c>DbContext</c>, so EF Core's implicit transaction covers both. That matters for
/// the same reason it does when sending an Angebot: a committed token link whose Invoice never
/// reached <c>Sent</c> is a live customer-facing credential for a bill nobody believes was issued,
/// and a <c>Sent</c> Invoice with no link is a customer who cannot see what they owe. **No explicit
/// transaction** — one save needs none.
/// </para>
/// <para>
/// <b>No PDF is generated.</b> Sequence Diagram §9 draws <c>IPdfGenerator</c> before the token step;
/// PDF generation is Phase 14's and no abstraction for it exists (approved decision G-4). FR-8.3
/// allows "a token link, by email as a PDF, or both", and this is the link.
/// </para>
/// <para>
/// <b>The Domain guard runs before the token is generated</b>, per Architecture §9's ordering
/// principle: <c>Invoice.Send()</c> self-guards <c>Draft</c> and <c>GrossAmount &gt; 0</c>, so a
/// rejected send produces no token at all. Nothing here is irreversible before the commit, but the
/// ordering is kept anyway — the same shape <c>SendAngebotCommandHandler</c> uses.
/// </para>
/// </summary>
public sealed class SendInvoiceCommandHandler(
    IValidator<SendInvoiceCommand> validator,
    IInvoiceRepository invoiceRepository,
    IProjectRepository projectRepository,
    ICustomerRepository customerRepository,
    ITokenLinkRepository tokenLinkRepository,
    ITokenLinkService tokenLinkService,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IEmailSender emailSender) : ICommandHandler<SendInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(SendInvoiceCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var invoice = await invoiceRepository.GetByIdAsync(command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), command.InvoiceId);

        // The recipient is reached through Project → Customer, which is how Sequence Diagram §9
        // addresses the mail (`to=Customer.Email`). Invoice references the Project by id only
        // (CLAUDE.md §2), so this is two lookups rather than a navigation.
        var project = await projectRepository.GetByIdAsync(invoice.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), invoice.ProjectId);

        var customer = await customerRepository.GetByIdAsync(project.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), project.CustomerId);

        // Self-guards: Draft, and a gross amount greater than zero (StateMachine.md §3.3). Runs
        // before a token exists, so a rejected send leaves no trace.
        invoice.Send();

        var generated = tokenLinkService.Generate();
        var tokenLink = TokenLink.Create(TokenLinkEntityType.Invoice, invoice.Id, generated.Token, generated.ExpiresAt);
        await tokenLinkRepository.AddAsync(tokenLink, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Logged against the Invoice, not the Project: unlike sending an Angebot — which drives
        // Lead.MarkAngebotSent() and is therefore a Lead-level milestone — this changes no other
        // aggregate's status at all. SRS FR-12.1 names "Invoice creation/status changes" in its own
        // right. After the commit, always (D50).
        await auditService.LogAsync(
            entityType: nameof(Invoice),
            entityId: invoice.Id,
            action: AuditAction.InvoiceSent,
            performedByUserId: command.SentByAdminId,
            details: $"Invoice {invoice.InvoiceNumber} sent to the customer.",
            cancellationToken);

        // After the commit, never before (CLAUDE.md §11) — this email hands the customer a working
        // credential, so it must never describe a state that failed to persist.
        await emailSender.SendInvoiceReadyNotificationAsync(
            new InvoiceReadyNotification(
                invoice.Id,
                invoice.InvoiceNumber,
                customer.Name,
                customer.Email,
                invoice.GrossAmount.Amount,
                invoice.DueDate,
                generated.Token),
            cancellationToken);

        return invoice.ToDto();
    }
}
