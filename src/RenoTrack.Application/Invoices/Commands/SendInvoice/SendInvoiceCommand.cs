using FluentValidation;

namespace RenoTrack.Application.Invoices.Commands.SendInvoice;

/// <summary>
/// SRS FR-8.3 / Sequence Diagram §9. Sends a <c>Draft</c> Invoice to the customer as a token link.
///
/// <para>
/// No request body exists for this action: the Invoice id comes from the route and the Admin from
/// the token's subject claim (D61). <c>SentByAdminId</c> is therefore server-derived, never caller
/// input — the same shape <c>SendAngebotCommand</c> has.
/// </para>
/// </summary>
public sealed record SendInvoiceCommand(int InvoiceId, int SentByAdminId);

public sealed class SendInvoiceCommandValidator : AbstractValidator<SendInvoiceCommand>
{
    public SendInvoiceCommandValidator()
    {
        RuleFor(c => c.InvoiceId).GreaterThan(0);
        RuleFor(c => c.SentByAdminId).GreaterThan(0);
    }
}
