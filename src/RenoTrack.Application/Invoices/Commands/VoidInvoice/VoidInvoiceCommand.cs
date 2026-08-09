using FluentValidation;

namespace RenoTrack.Application.Invoices.Commands.VoidInvoice;

/// <summary>
/// Cancels an Invoice (PermissionMatrix.md §5 "Void an Invoice", StateMachine.md §3.3). BR-9: the
/// row and its number are retained — voiding is a status, never a deletion.
///
/// <para>
/// <c>Reason</c> is the only body field. <c>InvoiceId</c> comes from the route and
/// <c>VoidedByAdminId</c> from the authenticated principal (D61).
/// </para>
/// </summary>
public sealed record VoidInvoiceCommand(int InvoiceId, string Reason, int VoidedByAdminId);

/// <summary>
/// Shape only. The reason's *presence* is checked here for a field-level 400, and again by
/// <c>Invoice.Void</c> which is the real backstop (CLAUDE.md §5) — `PermissionMatrix.md` §5 requires
/// one without qualification, and §3.3's `Draft → Void` row was reconciled to match in Slice 1.
/// </summary>
public sealed class VoidInvoiceCommandValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceCommandValidator()
    {
        RuleFor(c => c.InvoiceId).GreaterThan(0);
        RuleFor(c => c.Reason).NotEmpty();
        RuleFor(c => c.VoidedByAdminId).GreaterThan(0);
    }
}
