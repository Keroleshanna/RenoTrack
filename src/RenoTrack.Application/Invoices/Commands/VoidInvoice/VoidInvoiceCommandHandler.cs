using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Invoices.Commands.VoidInvoice;

/// <summary>
/// Cancels an Invoice (PermissionMatrix.md §5, StateMachine.md §3.3). Reachable from <c>Draft</c>,
/// <c>Sent</c> and <c>Overdue</c>; <c>Paid</c> and <c>Void</c> are terminal, so a paid Invoice can
/// never be voided and no voided Invoice ever carries a Payment.
///
/// <para>
/// <b>BR-9 is upheld structurally, not by discipline.</b> Nothing anywhere in this project deletes
/// an Invoice row, and <c>InvoiceNumber</c> has no mutator — so voiding preserves the number by
/// construction, which is exactly what BR-9 requires.
/// </para>
/// <para>
/// <b>No <c>IOwnershipValidator</c></b> — §5 marks the action Admin <c>F</c> (CLAUDE.md §16).
/// </para>
/// <para>
/// <b>One <c>SaveChangesAsync</c>, no explicit transaction</b> — a single status change on a single
/// aggregate.
/// </para>
/// <para>
/// <b>The reason is stored twice, and both are required by documents.</b> <c>Invoice.VoidReason</c>
/// holds it as business data; §3.3's side-effect column additionally specifies an "AuditLog entry
/// **with reason**", so it also goes into <c>details</c>. That is not duplication for its own sake:
/// the audit row records who cancelled the bill and why at that moment, while the invoice row
/// records the reason the document itself now carries.
/// </para>
/// <para>
/// <b>The balance effect needs no code.</b> §3.3 says a voided invoice is "excluded from 'remaining
/// balance' math going forward", and <c>ProjectQueries.GetInvoiceBalanceAsync</c> already filters
/// <c>Void</c> — so voiding raises <c>Remaining</c> with nothing written here.
/// </para>
/// <para>
/// <b>No notification and no Payment interaction.</b> No document describes either for this action.
/// </para>
/// </summary>
public sealed class VoidInvoiceCommandHandler(
    IValidator<VoidInvoiceCommand> validator,
    IInvoiceRepository invoiceRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<VoidInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(VoidInvoiceCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var invoice = await invoiceRepository.GetByIdAsync(command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), command.InvoiceId);

        // Self-guards: a reason is present, and the status is one of the three voidable ones.
        invoice.Void(command.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Invoice),
            entityId: invoice.Id,
            action: AuditAction.InvoiceVoided,
            performedByUserId: command.VoidedByAdminId,
            details: $"Invoice {invoice.InvoiceNumber} voided: {invoice.VoidReason}",
            cancellationToken);

        return invoice.ToDto();
    }
}
