using FluentValidation;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// SRS FR-8.1/FR-8.2, Sequence Diagram §8. Creates one Invoice against a Project; the Invoice
/// aggregate splits the Admin-entered gross across the originating Angebot's VAT rates.
///
/// <para>
/// <c>GrossAmount</c> and <c>DueDate</c> are the request body Sequence Diagram §8 specifies and
/// Wireframe E2 collects. <c>Description</c> and the optional service period were added in Phase 14
/// Slice 2 (D111), because the Invoice document must say what it bills for (BR-5).
/// <c>ProjectId</c> comes from the route and <c>CreatedByAdminId</c> from the authenticated
/// principal, never the body (D61).
/// </para>
/// <para>
/// <b>There is deliberately no net amount, VAT amount, rate or line here.</b> The Admin decides how
/// much this invoice bills; the Invoice aggregate decides how that gross splits by rate (D111).
/// </para>
/// </summary>
public sealed record CreateInvoiceCommand(
    int ProjectId,
    decimal GrossAmount,
    DateTime DueDate,
    string Description,
    DateOnly? ServicePeriodStart,
    DateOnly? ServicePeriodEnd,
    int CreatedByAdminId);

/// <summary>
/// Shape only, never business rules (CLAUDE.md §5).
///
/// <para>
/// <b>There is deliberately no maximum on <c>GrossAmount</c>.</b> BR-3 says the system "warns (does
/// not hard-block)" when invoices do not sum to the agreed total, so an invoice that exceeds the
/// remaining balance is a *valid request* whose consequence is a negative <c>Remaining</c> on the
/// balance read. A validator rule here would silently convert BR-3's warning into a prohibition.
/// </para>
/// <para>
/// <b>And no minimum above zero.</b> The Domain permits creating a zero-gross Invoice and refuses
/// only to *send* one (StateMachine.md §3.3 guards <c>GrossAmount &gt; 0</c> on <c>Draft → Sent</c>),
/// so rejecting zero at creation would invent a rule one state earlier than the documents place it.
/// Negative is rejected because a negative invoice is not a shape any document describes.
/// </para>
/// <para>
/// <b>And no constraint on <c>DueDate</c></b>, including against the issue date — no requirement
/// document places one.
/// </para>
/// <para>
/// <b>The description and service-period rules mirror <c>Invoice.Create</c>'s guards exactly</b>,
/// reading the same <see cref="Invoice.MaxDescriptionLength"/>. That is not duplication for its own
/// sake: the handler reserves an invoice number before calling <c>Invoice.Create</c> (D66), so a
/// shape error that only the Domain caught would burn a number on an ordinary bad request. The
/// Domain guard stays as the backstop.
/// </para>
/// </summary>
public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(c => c.ProjectId).GreaterThan(0);
        RuleFor(c => c.GrossAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.CreatedByAdminId).GreaterThan(0);

        // Measured after trimming, because the trimmed text is what will be stored.
        RuleFor(c => c.Description)
            .Must(d => !string.IsNullOrWhiteSpace(d))
            .WithMessage("A description is required.")
            .Must(d => d is null || d.Trim().Length <= Invoice.MaxDescriptionLength)
            .WithMessage($"A description may be at most {Invoice.MaxDescriptionLength} characters.");

        RuleFor(c => c.ServicePeriodStart)
            .NotNull()
            .When(c => c.ServicePeriodEnd is not null)
            .WithMessage("A service period end requires a start.");

        RuleFor(c => c.ServicePeriodEnd)
            .GreaterThanOrEqualTo(c => c.ServicePeriodStart)
            .When(c => c.ServicePeriodStart is not null && c.ServicePeriodEnd is not null)
            .WithMessage("A service period cannot end before it starts.");
    }
}
