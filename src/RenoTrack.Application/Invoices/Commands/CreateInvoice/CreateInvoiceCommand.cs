using FluentValidation;

namespace RenoTrack.Application.Invoices.Commands.CreateInvoice;

/// <summary>
/// SRS FR-8.1/FR-8.2, Sequence Diagram §8. Creates one Invoice against a Project, splitting the
/// Admin-entered gross across the originating Angebot's VAT rates.
///
/// <para>
/// <c>GrossAmount</c> and <c>DueDate</c> are the request body Sequence Diagram §8 specifies and
/// Wireframe E2 collects. <c>ProjectId</c> comes from the route and <c>CreatedByAdminId</c> from the
/// authenticated principal, never the body (D61).
/// </para>
/// </summary>
public sealed record CreateInvoiceCommand(
    int ProjectId,
    decimal GrossAmount,
    DateTime DueDate,
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
/// </summary>
public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(c => c.ProjectId).GreaterThan(0);
        RuleFor(c => c.GrossAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.CreatedByAdminId).GreaterThan(0);
    }
}
