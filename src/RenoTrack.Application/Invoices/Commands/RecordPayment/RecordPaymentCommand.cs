using FluentValidation;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Commands.RecordPayment;

/// <summary>
/// SRS FR-8.4 / Sequence Diagram §9, which names this command directly
/// (<c>Send(RecordPaymentCommand)</c>). The Admin confirms payment manually — there is no real
/// payment processing in v1.
///
/// <para>
/// <c>PaidAt</c> and <c>Method</c> are the request body §9 specifies and Wireframe E3 collects.
/// <c>InvoiceId</c> comes from the route and <c>RecordedByAdminId</c> from the authenticated
/// principal, never the body (D61).
/// </para>
/// <para>
/// <b>There is deliberately no amount.</b> Phase 8 records full payment only: the Payment always
/// carries the Invoice's own <c>GrossAmount</c>. Neither FR-8.4, nor §9's body, nor E3 offers an
/// amount to supply, so accepting one would invent a partial-payment capability whose consequences
/// no document defines.
/// </para>
/// </summary>
public sealed record RecordPaymentCommand(
    int InvoiceId,
    DateTime PaidAt,
    PaymentMethod Method,
    int RecordedByAdminId);

/// <summary>
/// Shape only (CLAUDE.md §5). <c>PaidAt</c> is deliberately unconstrained — no document places a
/// rule on it, and inventing one (a not-in-the-future guard, say) would be exactly the undocumented
/// business rule Slice 1 removed.
/// </summary>
public sealed class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(c => c.InvoiceId).GreaterThan(0);
        RuleFor(c => c.Method).IsInEnum();
        RuleFor(c => c.RecordedByAdminId).GreaterThan(0);
    }
}
