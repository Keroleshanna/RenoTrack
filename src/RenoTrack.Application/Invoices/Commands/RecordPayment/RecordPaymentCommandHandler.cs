using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Invoices.Commands.RecordPayment;

/// <summary>
/// SRS FR-8.4 / Sequence Diagram §9. Records the Admin's manual confirmation that an Invoice was
/// paid: the Invoice moves <c>Sent</c>/<c>Overdue</c> → <c>Paid</c> and a <c>Payment</c> child is
/// created in the same breath.
///
/// <para>
/// <b>No <c>IOwnershipValidator</c></b> — `PermissionMatrix.md` §5 marks "Mark Invoice Paid" Admin
/// <c>F</c> (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>One <c>SaveChangesAsync</c>, no explicit transaction.</b> The status change and the new
/// Payment row are two changes to *one* aggregate, tracked together — EF Core's implicit
/// transaction already covers them, and D48's explicit boundary exists for genuine multi-save
/// identity problems, not for symmetry.
/// </para>
/// <para>
/// <b>A duplicate payment is impossible rather than merely rejected.</b> <c>Invoice.MarkPaid</c>
/// self-guards <c>Sent</c>/<c>Overdue</c> and <c>Paid</c> is terminal, so a second confirmation is a
/// 409 from the aggregate — the handler adds no check of its own (CLAUDE.md §6).
/// </para>
/// <para>
/// <b>No notification.</b> FR-9.1 covers *sending*; FR-9.2 enumerates three Admin triggers, none of
/// them a payment; and §9's mark-paid segment draws no mail step. A notification is added only where
/// a document requires one (CLAUDE.md §11).
/// </para>
/// <para>
/// <b>Nothing recalculates a Project balance.</b> §3.3's side-effect column says "Project balance
/// recalculated", but no balance is stored — it is computed on read, and a <c>Sent</c> invoice
/// already counts toward <c>AlreadyInvoiced</c>, so marking it paid moves that figure by nothing.
/// There is no work here to do.
/// </para>
/// </summary>
public sealed class RecordPaymentCommandHandler(
    IValidator<RecordPaymentCommand> validator,
    IInvoiceRepository invoiceRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<RecordPaymentCommand, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var invoice = await invoiceRepository.GetByIdAsync(command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), command.InvoiceId);

        // The aggregate creates the Payment itself — its constructor is internal, so there is no
        // other way to bring one into existence, and the amount is always this Invoice's own gross.
        invoice.MarkPaid(command.Method, command.PaidAt, command.RecordedByAdminId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Invoice),
            entityId: invoice.Id,
            action: AuditAction.InvoicePaid,
            performedByUserId: command.RecordedByAdminId,
            details: $"Invoice {invoice.InvoiceNumber} marked paid ({command.Method}) on {command.PaidAt:yyyy-MM-dd}.",
            cancellationToken);

        return invoice.ToDto();
    }
}
