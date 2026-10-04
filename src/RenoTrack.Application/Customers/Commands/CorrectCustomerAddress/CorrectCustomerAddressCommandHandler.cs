using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Customers.Dtos;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Customers.Commands.CorrectCustomerAddress;

/// <summary>
/// Sets or corrects a Customer's address (BR-5, D112). The canonical CLAUDE.md §6 shape: validate,
/// load, invoke one Domain method, persist, audit, map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>InvoiceDocumentFactory</c> refuses an invoice whose customer has no
/// address, and a Customer converted from a website Lead never has one — the contact form does not
/// collect it, and a Customer is a copy that later Lead edits never reach. This is the only way to
/// give such a Customer the address BR-5 requires.
/// </para>
/// <para>
/// <b>No ownership check</b> — the row is Admin <c>F</c> (CLAUDE.md §16). <b>No notification</b> —
/// none is documented (CLAUDE.md §11). <b>No concurrency token</b>: the Domain method's guards read
/// only its argument, never stored state, so two corrections cannot race past a guard; the later
/// one simply wins, exactly as a Lead contact correction does (D112).
/// </para>
/// <para>
/// <b>Nothing about any Invoice changes here.</b> The document factory reads the Customer when it
/// renders, so a corrected address reaches every later render without touching an Invoice row.
/// </para>
/// </remarks>
public sealed class CorrectCustomerAddressCommandHandler(
    IValidator<CorrectCustomerAddressCommand> validator,
    ICustomerRepository customerRepository,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<CorrectCustomerAddressCommand, CustomerDto>
{
    public async Task<CustomerDto> HandleAsync(
        CorrectCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var customer = await customerRepository.GetByIdAsync(command.CustomerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), command.CustomerId);

        customer.CorrectAddress(command.Address);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditService.LogAsync(
            entityType: nameof(Customer),
            entityId: customer.Id,
            action: AuditAction.CustomerAddressCorrected,
            performedByUserId: command.CorrectedByAdminId,
            // No before/after address: AuditLog is not a field-level change log (see the enum value).
            details: null,
            cancellationToken);

        return customer.ToDto();
    }
}
