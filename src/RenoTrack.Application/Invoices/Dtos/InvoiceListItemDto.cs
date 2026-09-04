using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Dtos;

/// <summary>
/// One row of the Invoice list — the Rechnungen workspace and the Cockpit's receivables figures.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not <see cref="InvoiceDto"/>.</b> That is the create/transition response for one
/// Invoice the caller already has context for. A list is read without that context, so this carries
/// the customer's name (joined through Project → Customer in the projection) and <c>PaidAt</c>.
/// </para>
/// <para>
/// <b><c>PaidAt</c> is derived, not stored.</b> <c>Invoice</c> has no such column — payment is
/// recorded as a <c>Payment</c> child, and <c>MarkPaid</c> always creates exactly one for the full
/// gross amount (Phase 8 supports no partial payment). So the latest payment date *is* the paid
/// date, and the projection reads it rather than the schema gaining a denormalised field.
/// </para>
/// <para>
/// <b>There is no <c>IsOverdue</c> flag, and that is deliberate.</b> <c>InvoiceStatus.Overdue</c>
/// exists in the domain but nothing ever sets it — there is no scheduler and Phase 10 does not add
/// one. Whether a `Sent` invoice is late is <c>DueDate &lt; today</c>, which the caller can evaluate
/// from the fields already here. Materialising it as a boolean would look like persisted state.
/// </para>
/// </remarks>
public sealed record InvoiceListItemDto(
    int Id,
    string InvoiceNumber,
    int ProjectId,
    int CustomerId,
    string CustomerName,
    InvoiceStatus Status,
    DateTime IssueDate,
    DateTime DueDate,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    DateTime? PaidAt,
    string? VoidReason);

/// <summary>
/// The receivables position across every Invoice matching a filter — the Cockpit's financial band.
/// </summary>
/// <remarks>
/// <para>
/// Computed in SQL over the whole matching set, never by summing a page. A page total would silently
/// describe twenty-five rows while being labelled as the company's outstanding money, which is the
/// kind of wrong number a cockpit exists to avoid.
/// </para>
/// <para>
/// <b><c>Void</c> invoices are excluded from every figure except <see cref="VoidedGross"/>.</b> BR-9
/// keeps a voided invoice for its number, not as money owed — including it in "outstanding" would
/// invent a receivable the company has explicitly cancelled. It is reported separately rather than
/// hidden, so the totals reconcile against the raw invoice book.
/// </para>
/// <para>
/// <c>OverdueGross</c> is <c>Sent</c> with a due date in the past, evaluated against the caller's
/// supplied "as of" date — see the note on <see cref="InvoiceListItemDto"/> about the unset
/// <c>Overdue</c> status.
/// </para>
/// </remarks>
public sealed record ReceivablesSummaryDto(
    decimal InvoicedGross,
    decimal PaidGross,
    decimal OpenGross,
    decimal OverdueGross,
    decimal VoidedGross,
    int InvoiceCount,
    int OpenCount,
    int OverdueCount);
