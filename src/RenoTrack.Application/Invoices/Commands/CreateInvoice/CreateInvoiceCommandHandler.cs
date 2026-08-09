using FluentValidation;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Dtos;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// SRS FR-8.1/FR-8.2, Sequence Diagram §8. The Admin enters a gross amount and a due date; this
/// splits that gross across the originating Angebot's VAT rates and records the Invoice as
/// <c>Draft</c>.
///
/// <para>
/// <b>No <c>IOwnershipValidator</c>, deliberately.</b> `PermissionMatrix.md` §5 marks "Create
/// Invoice" Admin <c>F</c> — full access, not <c>S</c> — so an ownership call here would be the
/// semantic error CLAUDE.md §16 describes rather than merely redundant.
/// </para>
/// <para>
/// <b>Over-invoicing is not rejected here, and must not become so.</b> BR-3 warns rather than
/// blocks: the sum of a Project's invoices *should* equal its agreed total, and when it does not,
/// the system surfaces the discrepancy through <c>GetProjectInvoiceBalanceQuery</c> — whose
/// <c>Remaining</c> simply goes negative. There is no comparison against <c>AgreedTotal</c>
/// anywhere in this handler.
/// </para>
/// <para>
/// <b>One <c>SaveChangesAsync</c>, no explicit transaction.</b> A single insert is already atomic
/// under EF Core's implicit transaction; opening an explicit one (D48's amendment) would add a lock
/// scope for nothing. The number reservation is its own independently-committed statement by
/// design (D52) and is not part of this boundary — which is exactly why it is taken last.
/// </para>
/// </summary>
public sealed class CreateInvoiceCommandHandler(
    IValidator<CreateInvoiceCommand> validator,
    IProjectRepository projectRepository,
    IAngebotRepository angebotRepository,
    IInvoiceRepository invoiceRepository,
    INumberGeneratorService numberGenerator,
    IUnitOfWork unitOfWork,
    IAuditService auditService) : ICommandHandler<CreateInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(CreateInvoiceCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var project = await projectRepository.GetByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        // StateMachine.md §5: "An Invoice cannot exist without an Active/OnHold Project — foreign
        // key + guard clause in CreateInvoiceCommand." Assigned to this command by name, because
        // Invoice cannot see a Project (CLAUDE.md §2) and so could never guard it itself.
        if (project.Status == ProjectStatus.Completed)
        {
            throw new ConflictException(
                $"Project {project.Id} is completed; invoices can only be created against an Active or OnHold Project.");
        }

        // The rate mix comes from the originating Angebot (FR-8.2: "consistent with the originating
        // Angebot's rates"). GetByIdAsync loads the full aggregate, which is what makes
        // VatBreakdown computable — it is derived from the live section/item tree.
        var angebot = await angebotRepository.GetByIdAsync(project.AngebotId, cancellationToken)
            ?? throw new NotFoundException(nameof(Angebot), project.AngebotId);

        var requestedGross = Money.FromExact(command.GrossAmount);

        // A positive gross cannot be split proportionally across a rate mix that sums to zero —
        // there is no proportion. Rejected explicitly rather than allowed to surface as an
        // arithmetic fault, and kept as narrow as the arithmetic problem: a zero-gross Angebot
        // remains valid, a zero-valued Project remains valid, and a zero-gross Invoice against
        // either is still allowed (it needs no proportion, so it never reaches this branch).
        if (requestedGross != Money.Zero && angebot.GrossTotal == Money.Zero)
        {
            throw new ConflictException(
                $"Angebot {angebot.AngebotNumber} has a gross total of zero, so no VAT split can be derived for an invoice of {command.GrossAmount}.");
        }

        var allocation = VatAllocation.ProportionalTo(angebot.VatBreakdown, requestedGross);

        // Reserved last, after every guard that could reject this request has already passed
        // (D66). The reservation commits independently of the save below, so a failure after this
        // point leaves the number unused — the window is narrowed to the commit itself, not closed.
        var invoiceNumber = await numberGenerator.NextInvoiceNumberAsync(DateTime.UtcNow.Year, cancellationToken);

        var invoice = Invoice.Create(
            project.Id,
            invoiceNumber,
            command.DueDate,
            allocation.NetAmount,
            allocation.VatAmount,
            allocation.GrossAmount);

        await invoiceRepository.AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit, never before — AuditService shares this request's DbContext, so an
        // audit write with business changes still pending would flush them inside a try/catch that
        // swallows failures (D50).
        await auditService.LogAsync(
            entityType: nameof(Invoice),
            entityId: invoice.Id,
            action: AuditAction.InvoiceCreated,
            performedByUserId: command.CreatedByAdminId,
            details: $"Invoice {invoice.InvoiceNumber} created against Project {project.Id}.",
            cancellationToken);

        return invoice.ToDto();
    }
}
